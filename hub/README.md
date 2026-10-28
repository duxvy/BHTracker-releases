# BHTracker's Hub

The Hub tab in BHTracker shows Brawlhalla's latest news, what the newest patch changes for your legends, official esports
events and results, and countdowns. BHTracker never reads those websites itself: this folder's collector does, on GitHub
Actions, and puts everything into one small file, `hub.json`, on the `hub-data` branch. BHTracker downloads that file.
When a site changes, the collector is fixed here once and every player gets the fix without an app update.

## What runs
- `.github/workflows/hub.yml` runs the collector every 4 hours and on demand. It commits to `hub-data` only when
  `hub.json` or `date-suggestions.json` actually changed. Once a month it also re-enables itself, because GitHub switches
  off timed workflows in a repo with no activity for 60 days.
- No secrets. The job uses its own token, for this repo only.

## Where the content comes from
- **News and patch notes:** Brawlhalla's official posts on Steam (the public news API, app 291550; only Blue Mammoth's own
  announcements, not news sites' articles).
- **Esports:** upcoming official events from the same posts and from `dates.json`. Recent results come from
  [Liquipedia](https://liquipedia.net/brawlhalla/) (CC-BY-SA 3.0), which BHTracker credits wherever they show.
  The collector follows [Liquipedia's API terms](https://liquipedia.net/api-terms-of-use): the API only, at most one
  request every 2 seconds and one parse every 30, gzip, a User-Agent naming this repo, and each answer kept for 12 hours.
- **Countdowns:** `dates.json`, which the owner checks and approves. The collector only *suggests* dates it finds in
  posts (`date-suggestions.json` on `hub-data`); it never adds them itself.

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
