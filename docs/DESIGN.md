# Hearts: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Hearts for four: you, Rosie (left), Max (across) and Lena (right); a game to 100, lowest wins.
Pass three cards left, right, across, then hold. The two of clubs leads; follow suit if you can;
no points on the first trick unless you've nothing else; hearts can't be led until broken.
Hearts score 1, the queen of spades 13; take all 26 and everyone else scores 26. Rules in
`src/Core` (tested, including whole games).

## Feel
- Tap three cards to raise them, then Pass: the hands slide the cards face down across, and
  the three you received stand out for a moment.
- Cards you can't play are greyed. Each played card lands in front of its player in the middle;
  the winner's hand sweeps the trick onto their pile.

## Computer levels
Easy (any legal card), Medium (ducks, dumps the queen and high hearts, leads low), Hard (looks
ahead: deals the unseen cards many ways and plays each option through; runs on a background
thread).

## Scope for version 1
Everything above. Later: tutorial clips, the "Jack of diamonds" variant, moon-shot choice
(subtract 26 from yourself instead).
