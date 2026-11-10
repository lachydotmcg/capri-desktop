"""Bounded Responses text/refusal events; snapshots never duplicate deltas."""
from .broker import Denied


class ResponseFailure(Denied):
    def __init__(self, code, message, next_action="Send a new message when ready."):
        self.code = code
        super().__init__(f"[{code}] {message}" + (f" Next: {next_action}" if next_action else ""))


class ResponseRefusal(ResponseFailure):
    def __init__(self, text):
        self.text = text
        super().__init__("provider-refusal", text, "")


class ResponseStream:
    def __init__(self, emit):
        self.emit = emit
        self.parts = {}
        self.completed = False

    @property
    def text(self):
        return "".join(value for (_, _, kind), value in sorted(self.parts.items()) if kind == "output_text")

    async def part(self, kind, output, content, value, *, snapshot):
        if (type(output) is not int or type(content) is not int or not 0 <= output < 128
                or not 0 <= content < 128 or not isinstance(value, str)):
            raise ResponseFailure("provider-format", "Invalid response content.")
        key = (output, content, kind)
        previous = self.parts.get(key, "")
        if snapshot:
            if not value.startswith(previous):
                raise ResponseFailure("provider-format", "Response snapshot differs from its streamed text.")
            suffix = value[len(previous):]
        else:
            suffix = value
            value = previous + value
        if sum(map(len, self.parts.values())) - len(previous) + len(value) > 64000:
            raise ResponseFailure("provider-limit", "Response exceeds text limit.")
        self.parts[key] = value
        # Refusals are a distinct terminal result, never an action protocol.
        if kind == "output_text" and suffix:
            await self.emit(suffix)

    async def event(self, event):
        if not isinstance(event, dict):
            raise ResponseFailure("provider-format", "Invalid response event.")
        kind = event.get("type", "")
        if not isinstance(kind, str):raise ResponseFailure("provider-format", "Invalid event type.")
        if self.completed:
            if kind == "response.completed":
                return
            raise ResponseFailure("provider-format", "Content arrived after terminal completion.")
        if kind in ("response.function_call_arguments.done", "response.function_call_arguments.delta",
                    "response.custom_tool_call_input.done", "response.custom_tool_call_input.delta"):
            raise ResponseFailure("provider-tool-denied", "Unrequested provider tool call denied.")
        if kind in ("response.output_item.added", "response.output_item.done"):
            item = event.get("item", {})
            if not isinstance(item, dict):raise ResponseFailure("provider-format", "Invalid output item.")
            if item.get("type") in ("function_call", "custom_tool_call"):
                raise ResponseFailure("provider-tool-denied", "Unrequested provider tool call denied.")
        for content_kind in ("output_text", "refusal"):
            prefix = "response." + content_kind + "."
            if kind in (prefix + "delta", prefix + "done"):
                done = kind.endswith(".done")
                field = content_kind if content_kind == "refusal" else "text"
                await self.part(content_kind, event.get("output_index", 0), event.get("content_index", 0),
                                event.get(field if done else "delta", ""), snapshot=done)
                return
        if kind in ("error", "response.failed", "response.incomplete"):
            raise ResponseFailure("provider-incomplete" if kind.endswith("incomplete") else "provider-failed",
                                  "Provider ended the response without success; partial output may remain.",
                                  "Check connection/account limits before trying again; inspect any prior effects.")
        if kind == "response.completed":
            response = event.get("response", {})
            if not isinstance(response, dict):raise ResponseFailure("provider-format", "Invalid terminal response.")
            output = response.get("output", [])
            if not isinstance(output, list) or len(output) > 128:raise ResponseFailure("provider-format", "Invalid terminal output.")
            if response.get("status", "completed") != "completed":
                raise ResponseFailure("provider-incomplete", "Provider did not complete the response.")
            for i, item in enumerate(output):
                if not isinstance(item, dict):
                    raise ResponseFailure("provider-format", "Invalid response output.")
                if item.get("type") in ("function_call", "custom_tool_call"):
                    raise ResponseFailure("provider-tool-denied", "Unrequested provider tool call denied.")
                content = item.get("content", [])
                if not isinstance(content, list) or len(content) > 128:raise ResponseFailure("provider-format", "Invalid content snapshot.")
                for j, part in enumerate(content):
                    if not isinstance(part, dict):raise ResponseFailure("provider-format", "Invalid content part.")
                    part_kind = part.get("type")
                    if part_kind in ("output_text", "refusal"):
                        await self.part(part_kind, i, j, part.get("text" if part_kind == "output_text" else "refusal", ""), snapshot=True)
            self.completed = True

    def finish(self):
        if not self.completed:
            raise ResponseFailure("provider-interrupted", "Interrupted provider stream; no terminal completion.")
        refusal = "".join(value for (_, _, kind), value in sorted(self.parts.items()) if kind == "refusal")
        if refusal.strip():
            raise ResponseRefusal(refusal.strip())
        if not self.text.strip():
            raise ResponseFailure("provider-empty", "Provider completed without readable text.",
                                  "Send a new message; no automatic retry was made.")
        return self.text
