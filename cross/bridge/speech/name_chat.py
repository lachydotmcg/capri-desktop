"""Local Capri-name gate and the bounded follow-up window; no audio or network I/O."""
from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
import math
import re
import unicodedata

NAME = re.compile(r"(?<!\w)capri(?!\w)", re.IGNORECASE)


@dataclass(frozen=True)
class AcceptedLine:
    utterance: str
    text: str
    named: bool


class NameChat:
    """Only an actual name opens a chat. A blocked gate cannot reopen itself."""

    seconds = 20.0
    max_utterances = 64

    def __init__(self, clock):
        self.clock = clock
        self.until = -1.0
        self.opened = -1.0
        self.barred = False
        self.speaking = False
        self.busy = False
        self.name_only = False
        self.started = OrderedDict()
        self.finished = OrderedDict()

    @property
    def blocked(self):
        return self.barred or self.speaking

    @property
    def open(self):
        return not self.blocked and not self.name_only and self.clock() < self.until

    @property
    def mode(self):
        if self.blocked:
            return 'paused'
        return 'chatting' if self.open else 'name_only'

    def control(self, *, barred, speaking, busy, name_only=False):
        if any(type(value) is not bool for value in (barred, speaking, busy, name_only)):
            raise ValueError('Invalid listening context')
        now = self.clock()
        was_open = self.until >= 0 and now < self.until and not self.barred and not self.name_only
        # Speech capture is reset at both sides of an output-audio gate. A line
        # overlapping Capri's own playback cannot become a later human request.
        if self.blocked != (barred or speaking) or self.name_only != name_only:
            self.started.clear()
        self.barred, self.speaking, self.busy, self.name_only = barred, speaking, busy, name_only
        if barred or name_only:
            self.close()
        elif was_open and (speaking or busy):
            self.until = now + self.seconds
        self.tick()

    def tick(self):
        now = self.clock()
        # A reply/think that starts after expiry does not open a new chat.
        if not self.barred and not self.name_only and self.until >= 0 and now < self.until and (self.busy or self.speaking):
            self.until = now + self.seconds
        cutoff = now - 45.0
        while self.started and next(iter(self.started.values()))[0] < cutoff:
            self.started.popitem(last=False)

    def close(self):
        self.until = self.opened = -1.0
        self.started.clear()

    def speech_started(self, utterance, began):
        if not isinstance(utterance, str) or not 1 <= len(utterance) <= 256 or type(began) not in (int, float) or not math.isfinite(began):
            raise ValueError('Invalid utterance boundary')
        now = self.clock()
        began = min(now, float(began))
        if self.blocked or now - began > 45 or utterance in self.finished:
            return
        # Retain the expired deadline long enough for a VAD event whose acoustic
        # start was before it. Completion latency must not lose that utterance.
        eligible = not self.name_only and self.opened <= began < self.until and self.until >= 0
        self.started[utterance] = (began, eligible)
        self.started.move_to_end(utterance)
        while len(self.started) > self.max_utterances:
            self.started.popitem(last=False)

    def final(self, utterance, text):
        if not isinstance(text, str) or not 1 <= len(text.strip()) <= 4096:
            raise ValueError('Invalid final transcript')
        text = text.strip()
        boundary = self.started.pop(utterance, None)
        if utterance in self.finished:
            return None
        self.finished[utterance] = True
        while len(self.finished) > self.max_utterances:
            self.finished.popitem(last=False)
        if self.blocked:
            return None
        normalized = unicodedata.normalize('NFKC', text)
        named = bool(NAME.search(normalized))
        eligible = boundary is not None and boundary[1]
        # Never let an ambient unprefixed "stop" act as a global hotword. It is
        # accepted only in a conversation actually opened by hearing Capri.
        if not named and not eligible:
            return None
        if not named and self.name_only:
            return None
        if not self.name_only:
            if named and not self.open:
                self.opened = self.clock()
            self.until = self.clock() + self.seconds
        return AcceptedLine(utterance, text, named)
