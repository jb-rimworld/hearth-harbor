# Hearth & Harbor

Guests, hotels and hospitality for RimWorld, with the **Silverline** bank built in.

Mark a bed as a guest bed and travellers start to arrive. They check in, pay for the night, eat your food, lose silver at your
tables and leave a short, honest review. Everything is split into modules that you switch on or off per save, and every number
is a setting.

| Module | What it adds |
|---|---|
| Inn | guest beds priced by room class, a forecast of arrivals, front desk, guest wings, door policy, departure reports, renown |
| Shops & Food | Venue Sign (tavern, shop, restaurant, casino, spa, lounge, cinema, gallery), the Serving work type, shops, vending machines, market days, a kitchen with 100 dishes |
| Casino | slots, roulette, blackjack, Rimdeck poker with exact payout rates; The Lucky Thrumbo, an orbital casino where you gamble yourself |
| Wellness | hot tub, sauna, massage, day passes |
| City Life | cinema, salon, gallery, park, taxi stand, bodyguards |
| Underworld | off by default; back rooms, fight pit, fence, smuggling, gangs, Whispers and scandals |
| Silverline | six banks, interest, loans, 17 investments, a lottery, twelve insurance policies |
| DLC extras | Royalty, Ideology and Biotech content, plus 14 traits |

Silverline also ships as a standalone mod. When both are active, the standalone one steps aside.

## Versions

RimWorld **1.7** is the main target and is tested in game with automated suites for every module. 1.6 and 1.5 build from the
same source and are included as best effort. The only requirement is [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).

## Building

```bash
Source/build.sh all            # release build for 1.7, 1.6 and 1.5
Source/build.sh 1.7 --tests    # 1.7 with the in-game test harness
tools/test_matrix.sh m1        # every module's suites, each in its own game
tools/package.py               # release folders for both Workshop entries
```

`AGENTS.md` explains the layout, the module contract and the test tools. It is written for people and for AI agents who want to
change or reuse a module.

## License and credits

MIT, see `LICENSE`. Thanks to the authors whose mods inspired this one, see `CREDITS.md`. No code of other mods was copied.

This mod was built with the help of Claude Code.
