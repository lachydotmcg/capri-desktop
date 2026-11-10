"""Physical microphone identity filter retained from the Linux companion."""
import hashlib
import json

_REJECT = ("monitor", "loopback", "virtual", "echo-cancel", "null-audio")


def _text(value, maximum=256):
    return isinstance(value, str) and 0 < len(value) <= maximum and all(ord(c) >= 32 and ord(c) != 127 for c in value)


def physical_sources(rows):
    """Expose only bounded native Audio/Source nodes backed by ALSA/Bluetooth.

    This is an inventory eligibility check, not a claim about microphone distance
    or speaker identity. Node serials bind qualification to this device instance.
    """
    if not isinstance(rows, list) or len(rows) > 8192:
        raise ValueError("PipeWire inventory is not a bounded object list")
    devices = {}
    for row in rows:
        if not isinstance(row, dict) or row.get("type") != "PipeWire:Interface:Device":
            continue
        info = row.get("info")
        props = info.get("props") if isinstance(info, dict) else None
        if (type(row.get("id")) is int and row["id"] >= 0 and isinstance(props, dict)
                and isinstance(props.get("device.api"), str) and props["device.api"] in {"alsa", "bluez5", "bluez"}):
            devices[str(row.get("id"))] = props["device.api"]
    result, names, ambiguous = [], set(), set()
    for row in rows:
        if not isinstance(row, dict) or row.get("type") != "PipeWire:Interface:Node":
            continue
        info = row.get("info")
        props = info.get("props") if isinstance(info, dict) else None
        if not isinstance(props, dict) or props.get("media.class") != "Audio/Source":
            continue
        name, serial = props.get("node.name"), props.get("object.serial")
        if _text(name) and name in names:
            ambiguous.add(name)
        if (not _text(name) or name.startswith("-")
                or type(row.get("id")) is not int or row["id"] < 0
                or not 1 <= len(str(serial)) <= 20 or not str(serial).isascii() or not str(serial).isdigit()):
            continue
        names.add(name)
        description = props.get("node.description", name)
        if not _text(description):
            description = name
        labels = " ".join(str(props.get(key, "")) for key in
                          ("node.name", "node.description", "factory.name", "device.class")).casefold()
        if (any(marker in labels for marker in _REJECT)
                or any(str(props.get(key, "")).casefold() in {"true", "1", "yes"}
                       for key in ("node.virtual", "node.loopback"))):
            continue
        device_id = str(props.get("device.id"))
        api = devices.get(device_id)
        if api is None:
            continue
        identity = {"node": name, "id": row["id"], "serial": str(serial), "device_id": device_id, "device_api": api}
        fingerprint = hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()
        result.append({"node": name, "description": description, "media_class": "Audio/Source",
                       "device_api": api, "fingerprint": fingerprint,
                       "qualification": "reported-physical; near-field not measured"})
    return sorted((row for row in result if row["node"] not in ambiguous), key=lambda item: item["node"])



if __name__ == '__main__':
    import sys
    data = sys.stdin.buffer.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024: raise ValueError('Microphone inventory too large')
    print(json.dumps(physical_sources(json.loads(data))))
