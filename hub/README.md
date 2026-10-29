# BHTracker's Hub

The Hub tab in BHTracker shows Brawlhalla's news back to 2014, what each patch changed for every legend and weapon since
2016, official esports events and results, and countdowns. BHTracker never reads those websites itself: this folder's
collector does, on GitHub Actions, and puts everything on the `hub-data` branch: one small file, `hub.json`, which
BHTracker downloads, and the history, one file a year (`history/2026.json`), which it downloads only when its News list
or patch history opens. When a site changes, the collector is fixed here once and every player gets the fix without an
app update.

## What runs
- `.github/workflows/hub.yml` runs the collector every 4 hours and on demand. It commits to `hub-data` only when a file
  actually changed. Once a month it also re-enables itself, because GitHub switches off timed workflows in a repo with
  no activity for 60 days.
- The history is made from every official post once (Steam's news API paged back to 2014, a second between pages), and
  again whenever the collector's rules change (`History.Version`). After that, each run puts the newest posts in.
- No secrets. The job uses its own token, for this repo only.

## Where the content comes from
- **News and patch notes:** Brawlhalla's official posts on Steam (the public news API, app 291550; only Blue Mammoth's own
  announcements, not news sites' articles).
- **Esports:** upcoming official events from the same posts and from `dates.json`. Recent results come from
  [Liquipedia](https://liquipedia.net/brawlhalla/) (CC-BY-SA 3.0), which BHTracker credits wherever they show.
  The collector follows [Liquipedia's API terms](https://liquipedia.net/api-terms-of-use): the API only, at most one
  request every 2 seconds and one parse every 30, gzip, a User-Agent naming this repo, and each answer kept for 12 hours.
- **Countdowns:** the collector adds one by itself only when an official post states it for certain: one thing on its
  lists (a Battle Pass, a ranked season, an event, an official esports event) with a month and a day, as a span or as
  the day it starts or ends, and no other post saying otherwise (a newer post that changes it wins). It counts in days
  and never guesses an hour, and it links to the post. Anything less certain is only *suggested* (`date-suggestions.json`
  on `hub-data`) for the owner to check. The owner's `dates.json` always wins: their own entries replace the automatic
  ones, and its `removed` list takes one out.

## Summaries, not copies
`hub.json` holds only BHTracker's own summaries, facts and links. It never holds a sentence from anyone's post.
- A summary is built by plain code from names on fixed lists (legends, crossovers, events) and counts, for example
  "New legend: Qinghua & Baobao · Back to School event · 28 balance changes · 5 bug fixes".
- A balance change becomes a fact in a fixed format, like "Side Light: damage 12 → 14". ↑ or ↓ is shown only when the
  direction is certain. A change that isn't a clean fact names its legend or weapon and its move ("changed").
- Nothing is ever given to AI. Every item links to the full post. No images, logos or art.

## Running it locally
```
dotnet run --project hub/collector -- --out out --dates hub/dates.json --cache .cache
dotnet run --project hub/collector -- --check-dates hub/dates.json
```
