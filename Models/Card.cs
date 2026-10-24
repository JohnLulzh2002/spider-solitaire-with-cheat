namespace SpiderSolitaireWithCheat.Models;

public enum Suit
{
    Spades,
    Hearts,
    Clubs,
    Diamonds,
}

/// <summary>一张扑克牌。Rank: 1=A, 11=J, 12=Q, 13=K。</summary>
public sealed class Card
{
    public Suit Suit { get; }

    public int Rank { get; }

    public bool FaceUp { get; set; }

    public Card(Suit suit, int rank, bool faceUp = false)
    {
        Suit = suit;
        Rank = rank;
        FaceUp = faceUp;
    }

    public Card Clone() => new(Suit, Rank, FaceUp);

    public bool IsRed => Suit is Suit.Hearts or Suit.Diamonds;

    public char Glyph => Suit switch
    {
        Suit.Spades => '\u2660',
        Suit.Hearts => '\u2665',
        Suit.Clubs => '\u2663',
        _ => '\u2666',
    };

    public string RankText => Rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        _ => Rank.ToString(),
    };
}
