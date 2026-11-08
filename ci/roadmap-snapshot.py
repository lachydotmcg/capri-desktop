"""Writes site/roadmap/issues.json, the Roadmap page's built-in copy of the board.

The page reads GitHub's issues live. It only falls back to this copy when GitHub can't be reached
(offline, or a visitor past GitHub's 60 requests an hour), so refresh it with each release.
It picks issues the same way site/roadmap/roadmap.js does.

Run from the repo root, with the GitHub CLI signed in:  python ci/roadmap-snapshot.py
"""
import datetime
import json
import os
import re
import subprocess

REPO = 'lachydotmcg/capri-desktop'
SKIP = {'question', 'duplicate', 'invalid', 'wontfix'}
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'site', 'roadmap', 'issues.json')
# The same three patterns as roadmap.js: a GitHub name, a PR's "closes #12", and Lachy's "Suggested by ... on Discord." line.
GITHUB_NAME = re.compile(r'^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$')
CLOSING = re.compile(r'\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b:?\s*(?:lachydotmcg/capri-desktop)?#(\d+)', re.I)
SUGGESTED = re.compile(r'^[ \t]*Suggested\b[^\n]{0,160}', re.I | re.M)


def items(path, paginate):
    args = ['gh', 'api', path, '--jq', '.[]'] + (['--paginate'] if paginate else [])
    out = subprocess.run(args, check=True, capture_output=True, text=True, encoding='utf-8').stdout
    return [json.loads(line) for line in out.splitlines() if line.strip()]


def wanted(item):
    names = {label['name'] for label in item['labels']}
    return 'pull_request' not in item and names and not names & SKIP


def login(person):
    name = (person or {}).get('login') or ''
    return name if GITHUB_NAME.match(name) else ''


def pr_links(batch):
    # A PR that says "closes #12" credits its author on #12: open PRs on open issues, merged PRs on shipped ones.
    links = {}
    for item in batch:
        pull = item.get('pull_request')
        author = login(item.get('user'))
        if not pull or not author or (item.get('state') == 'closed' and not pull.get('merged_at')):
            continue
        for number in CLOSING.findall((item.get('title') or '') + '\n' + (item.get('body') or '')):
            names = links.setdefault(int(number), [])
            if author not in names:
                names.append(author)
    return links


def suggested(body):
    found = SUGGESTED.search(body or '')
    return found.group(0).strip() if found else ''


def normal(item, links):
    return {
        'n': item['number'],
        't': item['title'],
        'l': [label['name'] for label in item['labels']],
        'a': [name for name in (login(person) for person in item['assignees']) if name],
        'c': item['comments'],
        'at': item['created_at'],
        'done': item['closed_at'],
        'u': login(item.get('user')),
        'p': links.get(item['number'], []),
        's': suggested(item.get('body')),
    }


def main():
    now = datetime.datetime.now(datetime.timezone.utc)
    since = (now - datetime.timedelta(days=90)).strftime('%Y-%m-%dT%H:%M:%SZ')
    open_items = items(f'repos/{REPO}/issues?state=open&per_page=100', True)
    closed = items(f'repos/{REPO}/issues?state=closed&per_page=100&sort=updated&direction=desc&since={since}', False)
    open_links = pr_links(open_items)
    shipped_links = pr_links(closed)
    board = {
        'when': now.strftime('%Y-%m-%dT%H:%M:%SZ'),
        'open': [normal(item, open_links) for item in open_items if wanted(item)],
        'shipped': [normal(item, shipped_links) for item in closed if wanted(item) and item.get('state_reason') == 'completed'],
    }
    with open(OUT, 'w', encoding='utf-8', newline='\n') as file:
        json.dump(board, file, ensure_ascii=False, separators=(',', ':'))
        file.write('\n')
    print(f"{OUT}: {len(board['open'])} open, {len(board['shipped'])} shipped")


if __name__ == '__main__':
    main()
