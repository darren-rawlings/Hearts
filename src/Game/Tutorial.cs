using System.Collections.Generic;
using Renegade.Ui;

namespace Hearts.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("Avoid points",
			"Every heart you take scores 1 point against you, and the queen of spades 13. The lowest score wins when someone reaches 100."),
		new TutorialPage("Pass three",
			"Before each hand, tap three cards to pass, then press Pass: left, then right, then across; every fourth hand you keep your cards."),
		new TutorialPage("Play",
			"The two of clubs leads. Follow suit if you can (greyed cards can't be played). With none of the suit, play anything, and get rid of trouble. Hearts can't be led until one has been played, and nobody may play points on the first trick."),
		new TutorialPage("Shoot the moon",
			"Take every heart and the queen of spades, all 26 points, and everyone else scores 26 instead!"),
	};
}
