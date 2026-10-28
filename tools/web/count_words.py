#!/usr/bin/env python3
"""Checks the Devpost draft against the form limits: tagline <= 140 characters, story <= 500 words.
Review markers like `[P5]` are left out of the count."""
import re, sys
s = open(sys.argv[1]).read()
tag = re.search(r"\*\*Tagline[^:]*:\*\* (.+)", s).group(1).strip()
story = s.split("<!-- story:start -->")[1].split("<!-- story:end -->")[0]
story = re.sub(r"`\[[^\]]*\]`", "", story)
words = len(re.findall(r"[A-Za-z0-9][\w'’.+/%-]*", re.sub(r"[*#]", "", story)))
print(f"tagline: {len(tag)}/140 chars  story: {words}/500 words")
sys.exit(0 if len(tag) <= 140 and words <= 500 else 1)
