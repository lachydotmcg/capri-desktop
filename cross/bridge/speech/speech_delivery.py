"""Immutable, bounded presentation choices; never model/task authority."""
from dataclasses import dataclass
import re


@dataclass(frozen=True)
class Delivery:
    style: str = 'neutral'
    intensity: int = 0

    def __post_init__(self):
        if self.style not in ('neutral','playful','curious') or type(self.intensity) is not int or not 0 <= self.intensity <= 100:
            raise ValueError('Invalid phrase delivery')

    def payload(self):
        return {'style':self.style,'intensity':self.intensity}

    @property
    def rate(self):
        return 1 + {'neutral':0,'playful':.03,'curious':-.02}[self.style]*self.intensity/100

    @property
    def pause_ms(self):
        return round(100+40*self.intensity/100) if self.style!='neutral' and self.intensity else 0


def decode(value=None):
    if value is None:return Delivery()
    if not isinstance(value,dict)or set(value)!={'style','intensity'}:raise ValueError('Invalid phrase options')
    return Delivery(**value)


def plan(text, intensity, *, serious=False):
    if type(intensity)is not int or not 0 <= intensity <= 100:raise ValueError('Invalid expressiveness')
    # Keep exact results, paths, versions, technical names and errors steady.
    if (not intensity or serious or re.search(r'\d|[/\\_]|\b(?:error|failed|status|PID|Arasaka|Python|GiB|MiB)\b',text,re.I)):
        return Delivery()
    if text.rstrip().endswith('?'):return Delivery('curious',intensity)
    if re.match(r'^(?:Ha[h]?|Heh|Ooh|Whoa)\b',text,re.I)or text.rstrip().endswith('!'):
        return Delivery('playful',intensity)
    return Delivery()
