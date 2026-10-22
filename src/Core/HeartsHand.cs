using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Hearts.Core;

public enum Pass { Left, Right, Across, Hold }

public enum HeartsPhase { Passing, Playing, Over }

/// <summary>A trick that's been won: who took it, the cards, and the points in it.</summary>
public sealed record TrickWon(int Winner, IReadOnlyList<Play> Cards, int Points);

/// <summary>
/// One hand of Hearts for four (the usual rules). Thirteen cards each. Before play, everyone
/// passes three cards: to the left, then the right, then across, then the fourth hand is kept.
/// The holder of the two of clubs leads it. You must follow suit; with none of the suit led you
/// may play anything, except that no heart or queen of spades may go on the first trick unless
/// you've nothing else. Hearts can't be led until one has been played (unless you hold only
/// hearts). Each heart taken scores 1 and the queen of spades 13; take all 26 ("shoot the moon")
/// and everyone else scores 26 instead.
/// </summary>
public sealed class HeartsHand
{
	public const int Seats = 4;
	public static readonly Card TwoOfClubs = new(2, Suit.Clubs);
	public static readonly Card QueenOfSpades = new(Card.Queen, Suit.Spades);

	private readonly List<Card>[] _hands;
	private readonly List<Card>[] _passes = new List<Card>[Seats];
	private readonly List<Play> _trick = new();
	private readonly List<Card> _played = new();
	private readonly int[] _points = new int[Seats];
	private readonly int[] _tricks = new int[Seats];
	private readonly HashSet<Suit>[] _void = Enumerable.Range(0, Seats).Select(_ => new HashSet<Suit>()).ToArray();

	public HeartsHand(IReadOnlyList<Card> deck, int handNumber)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Hearts needs a full deck.", nameof(deck));
		Dealt = deck.ToList();
		_hands = Tricks.Deal(deck, Seats, first: handNumber % Seats);
		Direction = (Pass)(handNumber % 4);
		Phase = Direction == Pass.Hold ? HeartsPhase.Playing : HeartsPhase.Passing;
		if (Phase == HeartsPhase.Playing)
			Current = Holder(TwoOfClubs);
	}

	private HeartsHand(HeartsHand other, List<Card>[] hands)
	{
		Dealt = other.Dealt;
		_hands = hands;
		Direction = other.Direction;
		Phase = other.Phase;
		Current = other.Current;
		HeartsBroken = other.HeartsBroken;
		TricksPlayed = other.TricksPlayed;
		_trick.AddRange(other._trick);
		_played.AddRange(other._played);
		other._points.CopyTo(_points, 0);
		other._tricks.CopyTo(_tricks, 0);
		for (int s = 0; s < Seats; s++)
			_void[s].UnionWith(other._void[s]);
	}

	public IReadOnlyList<Card> Dealt { get; }
	public Pass Direction { get; }
	public HeartsPhase Phase { get; private set; }
	public int Current { get; private set; }
	public bool HeartsBroken { get; private set; }
	public int TricksPlayed { get; private set; }
	public bool FirstTrick => TricksPlayed == 0;
	public IReadOnlyList<Card> Hand(int seat) => _hands[seat];
	public IReadOnlyList<Play> Trick => _trick;
	public IReadOnlyList<Card> Played => _played;
	public int Points(int seat) => _points[seat];
	public int TricksWon(int seat) => _tricks[seat];

	/// <summary>Suits a player has shown they have none of (by not following).</summary>
	public IReadOnlySet<Suit> VoidIn(int seat) => _void[seat];

	/// <summary>The hand's final points per seat, with a moon shot turned round.</summary>
	public int[]? Final { get; private set; }

	/// <summary>The seat that took all 26, if anyone did.</summary>
	public int MoonShooter { get; private set; } = -1;

	public int PassTarget(int seat) => Direction switch
	{
		Pass.Left => (seat + 1) % Seats,
		Pass.Right => (seat + 3) % Seats,
		Pass.Across => (seat + 2) % Seats,
		_ => seat,
	};

	/// <summary>The three cards a seat received in the pass (after everyone has passed).</summary>
	public IReadOnlyList<Card> Received(int seat)
	{
		for (int s = 0; s < Seats; s++)
			if (_passes[s] != null && PassTarget(s) == seat)
				return _passes[s];
		return Array.Empty<Card>();
	}

	public bool HasPassed(int seat) => _passes[seat] != null;

	public static int PointsOf(Card card) => card.Suit == Suit.Hearts ? 1 : card == QueenOfSpades ? 13 : 0;

	private int Holder(Card card) => Enumerable.Range(0, Seats).First(s => _hands[s].Contains(card));

	/// <summary>Chooses three cards to pass. Once all four have chosen, they change hands and play begins.</summary>
	public void SetPass(int seat, IReadOnlyList<Card> cards)
	{
		if (Phase != HeartsPhase.Passing)
			throw new InvalidOperationException("Not passing now.");
		if (cards.Count != 3 || cards.Distinct().Count() != 3 || cards.Any(c => !_hands[seat].Contains(c)))
			throw new ArgumentException("Pass three cards from your hand.", nameof(cards));
		_passes[seat] = cards.ToList();
		if (_passes.Any(p => p == null))
			return;
		for (int s = 0; s < Seats; s++)
			foreach (var card in _passes[s])
				_hands[s].Remove(card);
		for (int s = 0; s < Seats; s++)
			_hands[PassTarget(s)].AddRange(_passes[s]);
		Phase = HeartsPhase.Playing;
		Current = Holder(TwoOfClubs);
	}

	/// <summary>The cards the current player may play now.</summary>
	public List<Card> Legal() => Legal(Current);

	public List<Card> Legal(int seat)
	{
		var hand = _hands[seat];
		if (_trick.Count == 0)
		{
			if (FirstTrick)
				return hand.Contains(TwoOfClubs) ? new List<Card> { TwoOfClubs } : hand.ToList();
			if (!HeartsBroken && hand.Any(c => c.Suit != Suit.Hearts))
				return hand.Where(c => c.Suit != Suit.Hearts).ToList();
			return hand.ToList();
		}
		var follow = Tricks.Following(hand, _trick[0].Card.Suit);
		if (FirstTrick && follow.Count > 0 && follow[0].Suit != _trick[0].Card.Suit)
		{
			// Void on the first trick: no points, unless that's all you hold.
			var safe = follow.Where(c => PointsOf(c) == 0).ToList();
			if (safe.Count > 0)
				return safe;
		}
		return follow;
	}

	/// <summary>The current player plays a card; returns the trick if this card completed it.</summary>
	public TrickWon? PlayCard(Card card)
	{
		if (Phase != HeartsPhase.Playing)
			throw new InvalidOperationException("Not playing now.");
		if (!Legal().Contains(card))
			throw new InvalidOperationException($"You can't play {card} now.");
		if (_trick.Count > 0 && card.Suit != _trick[0].Card.Suit)
			_void[Current].Add(_trick[0].Card.Suit);
		_hands[Current].Remove(card);
		_trick.Add(new Play(Current, card));
		_played.Add(card);
		if (card.Suit == Suit.Hearts)
			HeartsBroken = true;
		if (_trick.Count < Seats)
		{
			Current = (Current + 1) % Seats;
			return null;
		}
		var winner = Tricks.Winner(_trick).Seat;
		int points = _trick.Sum(p => PointsOf(p.Card));
		var won = new TrickWon(winner, _trick.ToList(), points);
		_points[winner] += points;
		_tricks[winner]++;
		_trick.Clear();
		TricksPlayed++;
		Current = winner;
		if (TricksPlayed == 13)
		{
			Phase = HeartsPhase.Over;
			MoonShooter = Array.IndexOf(_points, 26);
			Final = MoonShooter >= 0
				? Enumerable.Range(0, Seats).Select(s => s == MoonShooter ? 0 : 26).ToArray()
				: _points.ToArray();
		}
		return won;
	}

	/// <summary>An exact copy, to play forward without changing this one.</summary>
	public HeartsHand Clone() => new(this, _hands.Select(h => h.ToList()).ToArray());

	/// <summary>
	/// A copy where the cards this seat can't see are dealt out again at random among the other
	/// players, keeping their hand sizes and the suits they've shown void in (for the computer's
	/// look-ahead on Hard).
	/// </summary>
	public HeartsHand Guess(int seat, Random random)
	{
		var hidden = Enumerable.Range(0, Seats).Where(s => s != seat).SelectMany(s => _hands[s]).ToList();
		for (int attempt = 0; attempt < 40; attempt++)
		{
			Deck.Shuffle(hidden, random);
			var hands = new List<Card>[Seats];
			hands[seat] = _hands[seat].ToList();
			var pool = hidden.ToList();
			bool ok = true;
			foreach (int s in Enumerable.Range(0, Seats).Where(s => s != seat).OrderByDescending(s => _void[s].Count))
			{
				var fits = pool.Where(c => !_void[s].Contains(c.Suit)).Take(_hands[s].Count).ToList();
				if (fits.Count < _hands[s].Count)
				{
					ok = false;
					break;
				}
				hands[s] = fits;
				foreach (var c in fits)
					pool.Remove(c);
			}
			if (ok)
				return new HeartsHand(this, hands);
		}
		return new HeartsHand(this, _hands.Select(h => h.ToList()).ToArray());
	}
}

/// <summary>A game of Hearts: hands until someone reaches 100; the lowest score wins.</summary>
public sealed class HeartsGame
{
	public const int Limit = 100;

	public int[] Scores { get; } = new int[HeartsHand.Seats];
	public int Hands { get; private set; }
	public bool IsOver => Scores.Max() >= Limit;

	/// <summary>The players with the lowest score once the game is over (more than one is a tie).</summary>
	public IReadOnlyList<int> Leaders
	{
		get
		{
			int least = Scores.Min();
			return Enumerable.Range(0, HeartsHand.Seats).Where(s => Scores[s] == least).ToList();
		}
	}

	public void Record(HeartsHand hand)
	{
		var final = hand.Final ?? throw new InvalidOperationException("The hand isn't over.");
		for (int s = 0; s < HeartsHand.Seats; s++)
			Scores[s] += final[s];
		Hands++;
	}
}
