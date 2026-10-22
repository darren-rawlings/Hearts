using System;
using System.Collections.Generic;
using System.Linq;
using Hearts.Core;
using Renegade.Cards;
using Renegade.Core;
using Xunit;

namespace Hearts.Tests;

public class HeartsTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	private static void PassAll(HeartsHand hand, HeartsAi[] ais)
	{
		if (hand.Phase == HeartsPhase.Passing)
			foreach (var ai in ais)
				hand.SetPass(ai.Seat, ai.ChoosePass(hand));
	}

	private static HeartsAi[] Players(Random random, params Difficulty[] levels) =>
		Enumerable.Range(0, 4).Select(s => new HeartsAi(s, levels[s % levels.Length], random)).ToArray();

	[Fact]
	public void PassesGoLeftRightAcrossThenHold()
	{
		var deck = Deck.Shuffled(new Random(1));
		Assert.Equal(Pass.Left, new HeartsHand(deck, 0).Direction);
		Assert.Equal(Pass.Right, new HeartsHand(deck, 1).Direction);
		Assert.Equal(Pass.Across, new HeartsHand(deck, 2).Direction);
		var hold = new HeartsHand(deck, 3);
		Assert.Equal(HeartsPhase.Playing, hold.Phase);
		Assert.Equal(1, new HeartsHand(deck, 0).PassTarget(0));
		Assert.Equal(3, new HeartsHand(deck, 1).PassTarget(0));
	}

	[Fact]
	public void PassedCardsChangeHandsAndTheTwoOfClubsLeads()
	{
		var random = new Random(2);
		var hand = new HeartsHand(Deck.Shuffled(random), 0);
		var ais = Players(random, Difficulty.Medium);
		var passed = ais.Select(a => a.ChoosePass(hand)).ToArray();
		for (int s = 0; s < 4; s++)
			hand.SetPass(s, passed[s]);
		Assert.Equal(HeartsPhase.Playing, hand.Phase);
		for (int s = 0; s < 4; s++)
		{
			Assert.Equal(13, hand.Hand(s).Count);
			Assert.All(passed[s], c => Assert.Contains(c, hand.Hand(hand.PassTarget(s))));
		}
		Assert.Contains(HeartsHand.TwoOfClubs, hand.Hand(hand.Current));
		Assert.Equal(new[] { HeartsHand.TwoOfClubs }, hand.Legal());
	}

	[Fact]
	public void HeartsCantBeLedUntilBroken()
	{
		var random = new Random(3);
		for (int seed = 0; seed < 30; seed++)
		{
			var hand = new HeartsHand(Deck.Shuffled(new Random(seed)), 3);
			var ais = Players(random, Difficulty.Medium);
			while (hand.Phase == HeartsPhase.Playing)
			{
				var legal = hand.Legal();
				var mine = hand.Hand(hand.Current);
				if (hand.Trick.Count == 0 && !hand.HeartsBroken && mine.Any(c => c.Suit != Suit.Hearts))
					Assert.DoesNotContain(legal, c => c.Suit == Suit.Hearts);
				if (hand.FirstTrick && hand.Trick.Count > 0 && mine.All(c => c.Suit != hand.Trick[0].Card.Suit) && mine.Any(c => HeartsHand.PointsOf(c) == 0))
					Assert.DoesNotContain(legal, c => HeartsHand.PointsOf(c) > 0);
				hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
			}
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void EveryHandScoresTwentySixOrAMoonShot(int seed)
	{
		var random = new Random(seed);
		var game = new HeartsGame();
		var ais = Players(random, Difficulty.Easy, Difficulty.Medium);
		while (!game.IsOver)
		{
			var hand = new HeartsHand(Deck.Shuffled(random), game.Hands);
			PassAll(hand, ais);
			while (hand.Phase == HeartsPhase.Playing)
				hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
			Assert.Equal(26, Enumerable.Range(0, 4).Sum(hand.Points));
			Assert.Equal(13, Enumerable.Range(0, 4).Sum(hand.TricksWon));
			var final = hand.Final!;
			Assert.True(final.Sum() == 26 || (hand.MoonShooter >= 0 && final.Sum() == 78));
			game.Record(hand);
		}
		Assert.True(game.Scores.Max() >= 100);
		Assert.NotEmpty(game.Leaders);
	}

	[Fact]
	public void HardTakesFewerPointsThanEasy()
	{
		var random = new Random(9);
		int hard = 0, easy = 0;
		for (int n = 0; n < 24; n++)
		{
			// Seat 0 Hard against three Easy players.
			var ais = new[] { new HeartsAi(0, Difficulty.Hard, random), new HeartsAi(1, Difficulty.Easy, random), new HeartsAi(2, Difficulty.Easy, random), new HeartsAi(3, Difficulty.Easy, random) };
			var hand = new HeartsHand(Deck.Shuffled(random), n);
			PassAll(hand, ais);
			while (hand.Phase == HeartsPhase.Playing)
				hand.PlayCard(ais[hand.Current].ChoosePlay(hand));
			hard += hand.Final![0];
			easy += (hand.Final[1] + hand.Final[2] + hand.Final[3]) / 3;
		}
		Assert.True(hard < easy, $"Hard took {hard}, an Easy player {easy}");
	}
}
