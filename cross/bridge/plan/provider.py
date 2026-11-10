"""Official public ChatGPT-plan OAuth, strict terminal streaming, separate credentials.

No existing Codex/browser credentials are read. Authentication never grants tools.
"""
import asyncio
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import time
from urllib.parse import parse_qs, urlencode, urlparse
import uuid

from .broker import Denied

AUTH = "https://auth.openai.com"
API = "https://api.openai.com/v1"
SCOPE = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct"


def atomic_private(path, value):
    path = Path(path)
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    path.parent.chmod(0o700)
    temporary = path.with_name(path.name + "." + secrets.token_hex(8))
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, "w") as stream:
            json.dump(value, stream)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def private_json(path):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd) as stream:
        info = os.fstat(stream.fileno())
        if info.st_uid != os.getuid() or info.st_mode & 0o077:
            raise Denied("Credentials must be owned and mode 0600")
        return json.load(stream)


class PlanProvider:
    def __init__(self, directory):
        self.directory = Path(directory)
        self.directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        self.lock = asyncio.Lock()
        self.record = None
        self.catalog = []
        self.model = None
        self.http = None
        self.listener = None
        self.pending = None
        self.calls = 0
        hostpath = self.directory / "host.json"
        if not hostpath.exists():
            atomic_private(hostpath, {"id": "urn:uuid:" + str(uuid.uuid4())})
        self.host = private_json(hostpath)["id"]
        active = self.directory / "active.json"
        if active.exists():
            account = private_json(active)["account"]
            if not isinstance(account, str) or len(account) != 64 or any(c not in "0123456789abcdef" for c in account):
                raise Denied("Invalid active account record")
            self.record = private_json(self.directory / (account + ".json"))
            self.model = self.record.get("selected_model")

    @property
    def partition(self):
        if not self.record:
            return "offline-test"
        return hashlib.sha256((self.record["subject"] + self.record["client_id"]).encode()).hexdigest()

    async def client(self):
        if self.http is None:
            import httpx
            self.http = httpx.AsyncClient(timeout=30, follow_redirects=False, trust_env=False)
        return self.http

    async def request(self, method, url, **kwargs):
        if urlparse(url).netloc not in ("auth.openai.com", "api.openai.com") or urlparse(url).scheme != "https":
            raise Denied("Unapproved provider destination")
        response = await (await self.client()).request(method, url, **kwargs)
        if response.status_code >= 300:
            # Do not include credential-bearing URLs, headers or response bodies.
            raise Denied(f"Provider HTTP {response.status_code}; no fallback was attempted")
        return response.json()

    async def validate_identity(self, token, client_id, nonce=None):
        from authlib.jose import JsonWebToken
        from authlib.oidc.core import IDToken
        discovery = await self.request("GET", AUTH + "/.well-known/openid-configuration")
        jwks = await self.request("GET", discovery["jwks_uri"])
        options = {"iss": {"essential": True, "value": AUTH}, "aud": {"essential": True, "value": client_id},
                   "sub": {"essential": True}, "exp": {"essential": True}, "iat": {"essential": True}}
        if nonce is not None:
            options["nonce"] = {"essential": True, "value": nonce}
        claims = JsonWebToken(["RS256"]).decode(token, jwks, claims_cls=IDToken,
                       claims_options=options, claims_params={"client_id": client_id, "nonce": nonce})
        claims.validate(leeway=30)
        return dict(claims)

    async def save(self, record):
        account = hashlib.sha256((record["subject"] + record["client_id"]).encode()).hexdigest()
        atomic_private(self.directory / (account + ".json"), record)
        atomic_private(self.directory / "active.json", {"account": account})
        self.record = record
        self.catalog = []
        self.model = None

    async def open_browser(self, url):
        # Browser URL is carried over stdin/D-Bus, never argv, transcript or logs.
        proc = await asyncio.create_subprocess_exec("/usr/bin/python3", str(Path(__file__).with_name("portal_open.py")),
                     stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.DEVNULL, stderr=asyncio.subprocess.DEVNULL)
        try:
            await asyncio.wait_for(proc.communicate(url.encode()), 45)
        finally:
            if proc.returncode is None:
                proc.terminate()
                await proc.wait()
        if proc.returncode:
            raise Denied(f"System-browser portal did not open the sign-in (response {proc.returncode})")

    async def sign_in(self, notify):
        if self.pending:
            raise Denied("A browser sign-in is already pending")
        state, nonce, verifier = (secrets.token_urlsafe(32) for _ in range(3))
        future = asyncio.get_running_loop().create_future()
        previous = self.record
        expected_client = previous["client_id"] if previous else "dynamic_agent_client"
        self.pending = state

        async def callback(reader, writer):
            try:
                header = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 3)
                if len(header) > 8192:
                    raise Denied("Callback too large")
                method, target, _ = header.split(b"\r\n", 1)[0].decode("ascii").split(" ", 2)
                parsed = urlparse(target)
                params = parse_qs(parsed.query, keep_blank_values=True, max_num_fields=16)
                if method != "GET" or parsed.path != "/auth/callback" or any(len(v) != 1 for v in params.values()):
                    raise Denied("Invalid callback shape")
                if not secrets.compare_digest(params.get("state", [""])[0], state) or self.pending != state or future.done():
                    raise Denied("Callback state mismatch or replay")
                self.pending = None
                if "error" in params:
                    future.set_exception(Denied("Sign-in was declined or unavailable"))
                else:
                    client_id = params.get("client_id", [expected_client])[0]
                    if not client_id or len(client_id) > 200 or client_id == "dynamic_agent_client" or (previous and client_id != expected_client):
                        raise Denied("Issued client mismatch")
                    code = params.get("code", [""])[0]
                    if not code or len(code) > 4096:
                        raise Denied("Missing authorization code")
                    future.set_result((client_id, code))
                writer.write(b"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\nReturn to Capri. Account validation is still in progress.")
            except Exception:
                writer.write(b"HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\nInvalid callback.")
                if self.pending is None and not future.done():
                    future.set_exception(Denied("Sign-in callback rejected"))
            finally:
                await writer.drain()
                writer.close()
                await writer.wait_closed()

        self.listener = await asyncio.start_server(callback, "127.0.0.1", 0, limit=8192)
        port = self.listener.sockets[0].getsockname()[1]
        redirect = f"http://127.0.0.1:{port}/auth/callback"
        params = dict(client_id=expected_client, ext_agent_host_id=self.host, response_type="code",
                      redirect_uri=redirect, scope=SCOPE, resource=API, state=state, nonce=nonce,
                      code_challenge_method="S256", code_challenge=base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("="))
        if previous:
            params["id_token_hint"] = previous["id_token"]
        else:
            params["agent_name_hint"] = "Capri Desktop Companion"
        url = AUTH + "/api/accounts/authorize?" + urlencode(params)
        try:
            await self.open_browser(url)
            await notify("Browser accepted the sign-in request. Complete Continue with ChatGPT; no account is connected yet.")
            client_id, code = await asyncio.wait_for(future, 600)
            async with self.lock:
                token = await self.request("POST", AUTH + "/api/accounts/oauth/token", data={"grant_type": "authorization_code", "client_id": client_id,
                      "code": code, "code_verifier": verifier, "redirect_uri": redirect, "resource": API})
                claims = await self.validate_identity(token["id_token"], client_id, nonce)
                if previous and claims["sub"] != previous["subject"]:
                    raise Denied("Returning account identity mismatch; previous account retained")
                scopes = token.get("scope", "").split()
                if "chatgpt.tokens.use.direct" not in scopes:
                    raise Denied("Identity validated but ChatGPT plan permission was not granted")
                record = {**token, "subject": claims["sub"], "client_id": client_id,
                          "scopes": scopes, "expires_at": time.time() + token.get("expires_in", 3600)}
                await self.save(record)
            await self.models()
            return "ChatGPT account validated. Model catalog discovered; inference is not yet verified."
        finally:
            self.pending = None
            self.listener.close()
            await self.listener.wait_closed()
            self.listener = None

    async def access_token(self, min_validity=90):
        async with self.lock:
            if not self.record:
                raise Denied("Continue with ChatGPT first; offline test mode is active")
            if self.record["expires_at"] < time.time() + min_validity:
                token = await self.request("POST", AUTH + "/api/accounts/oauth/token", data={"grant_type": "refresh_token",
                      "client_id": self.record["client_id"], "refresh_token": self.record["refresh_token"], "resource": API})
                if "id_token" in token:
                    claims = await self.validate_identity(token["id_token"], self.record["client_id"])
                    if claims["sub"] != self.record["subject"]:
                        raise Denied("Refresh identity mismatch")
                record = {**self.record, **token, "expires_at": time.time() + token.get("expires_in", 3600)}
                if "scope" in token:
                    record["scopes"] = token["scope"].split()
                if "chatgpt.tokens.use.direct" not in record["scopes"]:
                    raise Denied("Plan usage permission is missing")
                self.record = record
                account = self.partition
                atomic_private(self.directory / (account + ".json"), record)
            return self.record["access_token"]

    async def models(self):
        token = await self.access_token()
        data = await self.request("GET", API + "/models", headers={"Authorization": "Bearer " + token})
        self.catalog = [{"id": row["slug"], "name": row["display_name"]} for row in data.get("models", []) if row.get("visibility") == "list"]
        if not self.catalog:
            raise Denied("No account models were returned")
        if self.model is not None and not any(row["id"] == self.model for row in self.catalog):
            raise Denied("Selected model is no longer available; choose from the refreshed account catalog")
        if self.model is None:
            await self.choose_model(self.catalog[0]["id"])
        return self.catalog

    async def choose_model(self, model, *, authorize=None):
        if not self.record or not any(row["id"] == model for row in self.catalog):
            raise Denied("Model is not in the active account catalog")
        async with self.lock:
            if authorize is not None:
                authorize()
            self.model = model
            self.record["selected_model"] = model
            atomic_private(self.directory / (self.partition + ".json"), self.record)

    async def respond(self, persona, context, delta, *, authorize=None, binding=None):
        partition = self.partition
        token = await self.access_token()
        if authorize is not None:authorize()
        if not self.catalog:
            await self.models()
            if authorize is not None:authorize()
        model = binding.model if binding is not None else self.model
        if binding is not None and (binding.provider != 'chatgpt_plan' or binding.partition != self.partition):
            raise Denied('Request belongs to a different provider account')
        if partition != self.partition or not any(row["id"] == model for row in self.catalog):
            raise Denied("Select an available account model before inference")
        body = {"model": model, "input": context, "instructions": persona, "store": False, "stream": True}
        if binding is not None:
            body.update(binding.fields())
        import httpx
        from .response_stream import ResponseStream, ResponseFailure
        stream = ResponseStream(delta)
        self.calls += 1
        try:
            async with (await self.client()).stream("POST", API + "/responses", headers={"Authorization": "Bearer " + token}, json=body) as response:
                if response.status_code != 200:
                    status = response.status_code
                    code = "provider-auth" if status in (401, 403) else "provider-rate-limit" if status == 429 else "provider-http"
                    raise ResponseFailure(code, f"Inference HTTP {status}; no paid/provider fallback.",
                                          "Check the connected account or service availability. No automatic retry was made.")
                async for line in response.aiter_lines():
                    if partition != self.partition:
                        raise Denied('Account changed during response streaming')
                    if authorize is not None:authorize()
                    if len(line) > 256000:
                        raise ResponseFailure("provider-limit", "Provider event too large.")
                    if not line.startswith("data: ") or line == "data: [DONE]":
                        continue
                    try:event = json.loads(line[6:])
                    except ValueError as error:
                        raise ResponseFailure("provider-format", "Invalid provider event JSON.") from error
                    await stream.event(event)
        except httpx.HTTPError as error:
            raise ResponseFailure("provider-network", "Response connection failed; partial output is not success.",
                                  "Check the connection, then send a new message. Inspect any earlier side effect first.") from error
        return stream.finish()

    async def respond_image(self, persona, question, frame, delta, *, authorize=None, context=None, binding=None):
        # Reuse the existing OAuth, endpoint and streaming terminal contract.
        # Only the ephemeral input content changes; no image enters stored history.
        from .image_input import responses_content
        if binding is None:
            return await self.respond(persona, responses_content(frame, question, context=context), delta, authorize=authorize)
        return await self.respond(persona, responses_content(frame, question, context=context), delta, authorize=authorize, binding=binding)

    async def logout(self):
        async with self.lock:
            if self.record:
                (self.directory / (self.partition + ".json")).unlink(missing_ok=True)
            (self.directory / "active.json").unlink(missing_ok=True)
            self.record = None
            self.catalog = []
            self.model = None
        return "Signed out locally. Remote token revocation was not confirmed."
