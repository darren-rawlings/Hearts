using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;

namespace Hearts.Core;

/// <summary>
/// A computer Hearts player. Easy plays any legal card. Medium plays like a careful club player:
/// it ducks under the winning card when points are about, gets rid of the queen of spades and
/// high hearts when it can't follow, and leads low. Hard looks ahead: for each card it could
/// play, it deals the unseen cards out many ways (keeping to what the table has shown) and plays
/// the hand through with Medium's style, choosing the card that costs it least on average.
/// </summary>
public sealed class HeartsAi
{
	private readonly Random _random;

	public HeartsAi(int seat, Difficulty difficulty, Random random)
	{
		Seat = seat;
		Difficulty = difficulty;
		_random = random;
	}

	public int Seat { get; }
	public Difficulty Difficulty { get; }

	/// <summary>The three cards to pass: the queen and high spades, high hearts, and whatever helps empty a short suit.</summary>
	public List<Card> ChoosePass(HeartsHand hand)
	{
		var mine = hand.Hand(Seat).ToList();
		if (Difficulty == Difficulty.Easy)
			return mine.OrderBy(_ => _random.Next()).Take(3).ToList();
		int spades = mine.Count(c => c.Suit == Suit.Spades);
		double Danger(Card c)
		{
			double d = Tricks.Value(c);
			if (c == HeartsHand.QueenOfSpades)
				d += spades >= 5 ? 0 : 30;
			else if (c.Suit == Suit.Spades && c.Rank is Card.King or Card.Ace)
				d += spades >= 5 ? 0 : 20;
			else if (c.Suit == Suit.Hearts)
				d += 4;
			// Cards from a short side suit go first, to make a void.
			if (c.Suit is Suit.Clubs or Suit.Diamonds && mine.Count(x => x.Suit == c.Suit) <= 2)
				d += 6;
			return d;
		}
		return mine.OrderByDescending(Danger).Take(3).ToList();
	}

	public Card ChoosePlay(HeartsHand hand)
	{
		var legal = hand.Legal();
		if (legal.Count == 1)
			return legal[0];
		return Difficulty switch
		{
			Difficulty.Easy => legal[_random.Next(legal.Count)],
			Difficulty.Medium => Heuristic(hand, hand.Current, legal),
			_ => LookAhead(hand, legal),
		};
	}

	/// <summary>Medium's choice, also used to play the Hard look-ahead through.</summary>
	public static Card Heuristic(HeartsHand hand, int seat, IReadOnlyList<Card> legal)
	{
		var trick = hand.Trick;
		bool queenOut = hand.Played.Contains(HeartsHand.QueenOfSpades);
		if (trick.Count == 0)
		{
			// Lead the lowest card, avoiding spades while the queen's still out if we hold high spades.
			bool highSpades = hand.Hand(seat).Any(c => c.Suit == Suit.Spades && c.Rank is Card.King or Card.Ace or Card.Queen);
			var safe = legal.Where(c => !(c.Suit == Suit.Spades && (!queenOut && (highSpades || Tricks.Value(c) >= 12)))).ToList();
			var from = safe.Count > 0 ? safe : legal.ToList();
			return from.OrderBy(Tricks.Value).ThenBy(c => c.Suit == Suit.Hearts ? 1 : 0).First();
		}
		var led = trick[0].Card.Suit;
		var winning = Tricks.Winner(trick);
		bool following = legal[0].Suit == led;
		int points = trick.Sum(p => HeartsHand.PointsOf(p.Card));
		bool last = trick.Count == HeartsHand.Seats - 1;
		if (following)
		{
			var under = legal.Where(c => Tricks.Value(c) < Tricks.Value(winning.Card)).ToList();
			if (under.Count > 0)
				return under.OrderByDescending(Tricks.Value).First(); // duck as high as we safely can
			if (last && points == 0)
				return legal.Where(c => c != HeartsHand.QueenOfSpades).DefaultIfEmpty(legal[0]).OrderByDescending(Tricks.Value).First();
			// We'll win it anyway: win with the lowest, and never with the queen if avoidable.
			return legal.Where(c => c != HeartsHand.QueenOfSpades).DefaultIfEmpty(legal[0]).OrderBy(Tricks.Value).First();
		}
		// Can't follow: dump the worst we hold.
		if (legal.Contains(HeartsHand.QueenOfSpades))
			return HeartsHand.QueenOfSpades;
		var bigSpade = legal.Where(c => c.Suit == Suit.Spades && c.Rank is Card.King or Card.Ace && !queenOut).ToList();
		if (bigSpade.Count > 0)
			return bigSpade[0];
		var hearts = legal.Where(c => c.Suit == Suit.Hearts).ToList();
		if (hearts.Count > 0)
			return hearts.OrderByDescending(Tricks.Value).First();
		return legal.OrderByDescending(Tricks.Value).First();
	}

	private Card LookAhead(HeartsHand hand, IReadOnlyList<Card> legal)
	{
		const int samples = 28;
		var cost = legal.ToDictionary(c => c, _ => 0.0);
		for (int n = 0; n < samples; n++)
		{
			var world = hand.Guess(Seat, _random);
			foreach (var card in legal)
			{
				var sim = world.Clone(); // the same imagined deal for every card we compare
				sim.PlayCard(card);
				while (sim.Phase == HeartsPhase.Playing)
					sim.PlayCard(Heuristic(sim, sim.Current, sim.Legal()));
				var final = sim.Final!;
				// What we score, less what the others score on average (a shot moon is great for us).
				double mine = final[Seat];
				double others = Enumerable.Range(0, HeartsHand.Seats).Where(s => s != Seat).Average(s => final[s]);
				cost[card] += mine - 0.3 * others;
			}
		}
		return cost.OrderBy(kv => kv.Value).ThenBy(kv => Tricks.Value(kv.Key)).First().Key;
	}
}
