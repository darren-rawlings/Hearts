using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hearts.Core;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;

namespace Hearts.Game;

/// <summary>
/// Hearts with three computer players (Rosie on your left, Max across, Lena on your right), a
/// game to 100. Pick three cards to pass by tapping them, then press Pass. On your turn, tap a
/// card to play it (cards you can't play are greyed). Each trick's winner sweeps it to their pile.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private HeartsGame _game = new();
	private HeartsHand? _hand;
	private readonly HeartsAi[] _ais = new HeartsAi[4];
	private readonly List<Card3D> _trick = new();
	private Button _pass = null!;

	protected override string Title => "Hearts";
	protected override string Hint => "Tap a card to play it";
	protected override int MinPlayers => 4;
	protected override int MaxPlayers => 4;
	protected override bool SortYourHand => false;
	protected override string NewGameText => _game.IsOver ? "New game" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_game.IsOver && (_game.Hands > 0 || _hand is { Phase: not HeartsPhase.Over });

	protected override void LevelChanged(Difficulty difficulty) => MakeAis(difficulty);

	private void MakeAis(Difficulty difficulty)
	{
		for (int s = 0; s < 4; s++)
			_ais[s] = new HeartsAi(s, s == 0 ? Difficulty.Medium : difficulty, new Random(Random.Next()));
	}

	protected override void AddSettings()
	{
		_pass = Hud.AddButton("Pass", true, Control.LayoutPreset.CenterBottom, -170, 170, -192, -128, 28);
		_pass.Visible = false;
		_pass.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: HeartsPhase.Passing } && Fans[0].RaisedCards.Count == 3)
				Run(DoPass(Fans[0].RaisedCards.Select(n => n.Card).ToList()));
		};
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_pass.Visible = false;
		if (_game.IsOver)
			_game = new HeartsGame();
		MakeAis(Settings.Difficulty);
		_nodes.Clear();
		_trick.Clear();
		_hand = null;
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var hand = new HeartsHand(order, _game.Hands);
		_hand = hand;
		int first = _game.Hands % 4;
		await DealRound(nodes, Enumerable.Range(0, 52).Select(i => (first + i) % 4).ToList(), spacing: 0.045f);
		if (round != Round)
			return;
		await PickUpAll(s => hand.Hand(s).Select(c => _nodes[c]));
		ArrangeBySuit();
		Hud.SetRestartVisible(true);
		if (hand.Phase == HeartsPhase.Passing)
		{
			if (Demo)
			{
				await DoPass(_ais[0].ChoosePass(hand));
				return;
			}
			Busy = false;
			_pass.Text = $"Pass {PassName(hand.Direction)}";
			_pass.Visible = true;
			_pass.Disabled = true;
			Hud.SetStatus($"Choose three cards to pass {PassName(hand.Direction)}: tap them.", Bright);
			return;
		}
		Hud.SetStatus("This hand, everyone keeps their cards.", Muted);
		await Pause(0.8f);
		await NextTurn();
	}

	private static string PassName(Pass pass) => pass switch { Pass.Left => "left", Pass.Right => "right", Pass.Across => "across", _ => "" };

	private async Task DoPass(IReadOnlyList<Card> mine)
	{
		var hand = _hand!;
		Busy = true;
		_pass.Visible = false;
		Fans[0].LowerAll();
		var chosen = new List<Card>[4];
		chosen[0] = mine.ToList();
		for (int s = 1; s < 4; s++)
			chosen[s] = _ais[s].ChoosePass(hand);
		for (int s = 0; s < 4; s++)
			hand.SetPass(s, chosen[s]);
		Hud.SetStatus($"Everyone passes three cards {PassName(hand.Direction)}.", Muted);
		// Each player slides their three face down towards the one they pass to, who picks them up.
		var moves = new List<Task>();
		for (int s = 0; s < 4; s++)
		{
			var cards = chosen[s].Select(c => _nodes[c]).ToList();
			foreach (var node in cards)
				Fans[s].Remove(node);
			Fans[s].Settle();
			int to = hand.PassTarget(s);
			var spot = Table.Landing[to];
			var targets = cards.Select((_, i) => FaceDownAt(spot + new Vector3(i * 0.1f, 0.004f * (i + 1), 0), 0.2f * i)).ToList();
			moves.Add(Hands[s].Carry(cards, targets, seconds: 0.4f, lift: 0.25f));
		}
		await Task.WhenAll(moves);
		var pickups = new List<Task>();
		for (int s = 0; s < 4; s++)
			pickups.Add(IntoFan(s, hand.Received(s).Select(c => _nodes[c]).ToList()));
		await Task.WhenAll(pickups);
		ArrangeBySuit();
		// Your three new cards stand out for a moment.
		foreach (var card in hand.Received(0))
			Fans[0].RaiseCard(_nodes[card], true);
		Fans[0].Settle(0.2f);
		var from = Enumerable.Range(1, 3).First(s => hand.PassTarget(s) == 0);
		News($"{Names[from]} passed you these.");
		await Pause(1.4f);
		Fans[0].LowerAll();
		Fans[0].Settle(0.2f);
		await NextTurn();
	}

	private async Task NextTurn()
	{
		var hand = _hand!;
		int round = Round;
		SetScore();
		if (hand.Phase == HeartsPhase.Over)
		{
			EndHand();
			return;
		}
		int seat = hand.Current;
		ShowTurn(seat);
		if (seat == 0 && !Demo)
		{
			var legal = hand.Legal();
			DimUnplayable(legal);
			Busy = false;
			Hud.SetStatus(hand.Trick.Count == 0
				? (hand.FirstTrick ? "Lead the two of clubs." : "Your lead: tap a card.")
				: "Your turn: tap a card to play.", Bright);
			return;
		}
		Busy = true;
		Hud.SetStatus(seat == 0 ? "Your turn." : $"{Names[seat]}'s turn.", Muted);
		var copy = hand.Clone();
		var ai = _ais[seat];
		var think = Task.Run(() => ai.ChoosePlay(copy));
		await Pause(0.45f);
		var card = await think;
		if (round != Round)
			return;
		await Play(seat, card);
	}

	private async Task Play(int seat, Card card)
	{
		var hand = _hand!;
		Busy = true;
		DimUnplayable(null);
		bool breaks = card.Suit == Suit.Hearts && !hand.HeartsBroken;
		var won = hand.PlayCard(card);
		var node = _nodes[card];
		_trick.Add(node);
		await PlayToTrick(seat, node);
		if (seat == 0)
			ArrangeBySuit();
		if (breaks)
			News("Hearts are broken!");
		else if (card == HeartsHand.QueenOfSpades)
			News(seat == 0 ? "You play the queen of spades!" : $"{Names[seat]} plays the queen of spades!");
		if (won != null)
		{
			await Pause(0.7f);
			string pts = won.Points == 0 ? "no points" : won.Points == 1 ? "1 point" : $"{won.Points} points";
			TrickNews(won.Cards, won.Winner, won.Points > 0 ? pts : null);
			await SweepTrick(won.Winner, _trick.ToList(), hand.TricksWon(won.Winner) - 1);
			_trick.Clear();
		}
		await NextTurn();
	}

	private void EndHand()
	{
		var hand = _hand!;
		_game.Record(hand);
		SetScore();
		string moon = hand.MoonShooter >= 0 ? (hand.MoonShooter == 0 ? "You shot the moon! Everyone else takes 26. " : $"{Names[hand.MoonShooter]} shot the moon! ") : "";
		string mine = $"You took {hand.Points(0)} this hand.";
		if (_game.IsOver)
		{
			var leaders = _game.Leaders;
			int winner = leaders.Count == 1 ? leaders[0] : -1;
			string result = winner == 0 ? $"You win with {_game.Scores[0]}!" : winner > 0 ? $"{Names[winner]} wins with {_game.Scores[winner]}." : "A tie for the lowest score!";
			if (winner >= 0)
				Sparkle.Burst(this, Table.Winnings[winner] + Vector3.Up * 0.3f);
			Finish($"{moon}{result}", leaders.Contains(0) ? 0 : 1, leaders.Contains(0) ? AllWonBy(0) : null, Table.Winnings[0]);
		}
		else
		{
			Finish($"{moon}{mine}", -1);
		}
	}

	private IEnumerable<Card3D> AllWonBy(int seat) =>
		GetChildren().OfType<Card3D>().Where(n => n.GlobalPosition.DistanceTo(Table.Winnings[seat]) < 0.8f).Take(8);

	private void SetScore()
	{
		var scores = Enumerable.Range(0, 4).Select(s => $"{(s == 0 ? "you" : Names[s])} {_game.Scores[s] + (_hand is { Phase: not HeartsPhase.Over } h ? h.Points(s) : 0)}");
		Hud.SetScore($"To 100: {string.Join(" · ", scores)}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_hand is not { } hand || Fans[0].At(origin, direction) is not { } card)
			return;
		if (hand.Phase == HeartsPhase.Passing)
		{
			bool raise = !Fans[0].IsRaised(card);
			if (raise && Fans[0].RaisedCards.Count >= 3)
				return;
			Fans[0].RaiseCard(card, raise);
			Fans[0].Settle(0.12f);
			_pass.Disabled = Fans[0].RaisedCards.Count != 3;
			int left = 3 - Fans[0].RaisedCards.Count;
			Hud.SetStatus(left == 0 ? $"Press Pass to send them {PassName(hand.Direction)}." : $"Choose {left} more to pass {PassName(hand.Direction)}.", Bright);
			return;
		}
		if (hand.Phase != HeartsPhase.Playing || hand.Current != 0)
			return;
		if (!hand.Legal().Contains(card.Card))
		{
			Hud.SetStatus(hand.Trick.Count > 0 && hand.Hand(0).Any(c => c.Suit == hand.Trick[0].Card.Suit)
				? "You must follow suit."
				: hand.Trick.Count == 0 && !hand.HeartsBroken ? "Hearts aren't broken yet: lead another suit."
				: "No points on the first trick.", Bright);
			return;
		}
		Hud.HideHint();
		Run(Play(0, card.Card));
	}
}
