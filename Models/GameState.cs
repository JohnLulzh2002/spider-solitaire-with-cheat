namespace SpiderSolitaireWithCheat.Models;

/// <summary>
/// 蜘蛛纸牌游戏状态与规则引擎。
/// 104 张牌（2 副），10 列 tableau，50 张存放在 stock 中分 5 次发牌。
/// </summary>
public sealed class GameState
{
    public const int ColumnCount = 10;
    public const int TotalSequences = 8;
    public const int StockDealSize = 10;
    public const int SequenceLength = 13;

    public List<List<Card>> Tableau { get; } = new(
        Enumerable.Range(0, ColumnCount).Select(_ => new List<Card>()));

    public List<Card> Stock { get; } = new();

    /// <summary>已完成序列的花色（每完成一组 K→A 加入一个）。</summary>
    public List<Suit> Foundations { get; } = new();

    public int Score { get; set; } = 500;

    public int Moves { get; set; }

    /// <summary>难度：1 / 2 / 4 花色。</summary>
    public int Suits { get; }

    public bool IsWon => Foundations.Count >= TotalSequences;

    private GameState(int suits) => Suits = suits;

    /// <summary>发一局新牌。列 0-3 各 6 张，列 4-9 各 5 张（共 54 张），每列顶牌翻开。</summary>
    public static GameState DealNew(int suits, Random rng)
    {
        var g = new GameState(suits);
        var deck = new List<Card>(104);
        int setsPerSuit = 8 / suits;
        Suit[] order = suits switch
        {
            1 => [Suit.Spades],
            2 => [Suit.Spades, Suit.Hearts],
            _ => Enum.GetValues<Suit>(),
        };
        foreach (var s in order)
        {
            for (int set = 0; set < setsPerSuit; set++)
            {
                for (int r = 1; r <= 13; r++)
                {
                    deck.Add(new Card(s, r));
                }
            }
        }

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        int pos = 0;
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < ColumnCount; col++)
            {
                if (row >= 5 && col >= 4)
                {
                    continue; // 前 4 列 6 张，其余 5 张
                }

                g.Tableau[col].Add(deck[pos++]);
            }
        }

        foreach (var pile in g.Tableau)
        {
            pile[^1].FaceUp = true;
        }

        for (; pos < deck.Count; pos++)
        {
            g.Stock.Add(deck[pos]);
        }

        return g;
    }

    /// <summary>从 col 列 index 起到末尾的牌是否构成可拾取的同花降序序列（且全部明牌）。</summary>
    public bool CanPickUp(int col, int index)
    {
        var pile = Tableau[col];
        if (index < 0 || index >= pile.Count)
        {
            return false;
        }

        for (int i = index; i < pile.Count; i++)
        {
            if (!pile[i].FaceUp)
            {
                return false;
            }

            if (i > index && (pile[i].Suit != pile[i - 1].Suit || pile[i].Rank != pile[i - 1].Rank - 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>把 fromCol 列 index 起的序列放到 toCol 列是否合法。</summary>
    public bool CanDrop(int fromCol, int index, int toCol)
    {
        if (fromCol == toCol || index < 0 || index >= Tableau[fromCol].Count)
        {
            return false;
        }

        var target = Tableau[toCol];
        if (target.Count == 0)
        {
            return true;
        }

        return target[^1].Rank == Tableau[fromCol][index].Rank + 1;
    }

    /// <summary>若把 run 移到 toCol，是否会立即完成一组 K→A 同花序列。</summary>
    public bool WouldComplete(int fromCol, int index, int toCol)
    {
        var from = Tableau[fromCol];
        var run = from.GetRange(index, from.Count - index);
        if (run.Count == 0 || run[^1].Rank != 1)
        {
            return false; // 序列必须以 A 收尾
        }

        int need = SequenceLength - run.Count;
        var target = Tableau[toCol];
        if (need > target.Count)
        {
            return false;
        }

        for (int i = 0; i < need; i++)
        {
            var c = target[target.Count - need + i];
            if (!c.FaceUp || c.Suit != run[0].Suit || c.Rank != run[0].Rank + 1 + i)
            {
                return false;
            }
        }

        return true;
    }

    public bool MoveCards(int fromCol, int index, int toCol, bool force = false)
    {
        if (force)
        {
            // 强制移牌：只要求是明牌，不限同花降序与目标点数
            var source = Tableau[fromCol];
            if (fromCol == toCol || index < 0 || index >= source.Count)
            {
                return false;
            }

            for (int i = index; i < source.Count; i++)
            {
                if (!source[i].FaceUp)
                {
                    return false;
                }
            }
        }
        else if (!CanPickUp(fromCol, index) || !CanDrop(fromCol, index, toCol))
        {
            return false;
        }

        var from = Tableau[fromCol];
        int count = from.Count - index;
        var moving = from.GetRange(index, count);
        from.RemoveRange(index, count);
        Tableau[toCol].AddRange(moving);
        FlipTop(fromCol);
        Score = Math.Max(0, Score - 1);
        Moves++;
        return true;
    }

    private void FlipTop(int col)
    {
        var pile = Tableau[col];
        if (pile.Count > 0 && !pile[^1].FaceUp)
        {
            pile[^1].FaceUp = true;
        }
    }

    /// <summary>存在空列时不能发牌（经典规则）。</summary>
    public bool CanDeal => Stock.Count >= StockDealSize && Tableau.All(p => p.Count > 0);

    /// <summary>向每列发一张牌（共 10 张）。force 为 true 时不做空列限制。</summary>
    public bool DealRow(bool force = false)
    {
        if (!CanDeal && !force)
        {
            return false;
        }

        for (int i = 0; i < ColumnCount; i++)
        {
            var card = Stock[^1];
            Stock.RemoveAt(Stock.Count - 1);
            card.FaceUp = true;
            Tableau[i].Add(card);
        }

        return true;
    }

    /// <summary>检查所有列尾部是否形成 K→A 同花序列，移除并收入 foundation。返回完成的花色。</summary>
    public List<Suit> CollectCompletedSequences()
    {
        var completed = new List<Suit>();
        for (int col = 0; col < ColumnCount; col++)
        {
            var pile = Tableau[col];
            if (pile.Count < SequenceLength)
            {
                continue;
            }

            var suit = pile[^1].Suit;
            bool ok = true;
            for (int i = 0; i < SequenceLength; i++)
            {
                var c = pile[pile.Count - SequenceLength + i];
                if (!c.FaceUp || c.Suit != suit || c.Rank != 13 - i)
                {
                    ok = false;
                    break;
                }
            }

            if (!ok)
            {
                continue;
            }

            pile.RemoveRange(pile.Count - SequenceLength, SequenceLength);
            Foundations.Add(suit);
            Score += 100;
            FlipTop(col);
            completed.Add(suit);
        }

        return completed;
    }

    /// <summary>深拷贝（用于撤销快照）。</summary>
    public GameState Clone()
    {
        var g = new GameState(Suits) { Score = Score, Moves = Moves };
        for (int i = 0; i < ColumnCount; i++)
        {
            g.Tableau[i].AddRange(Tableau[i].Select(c => c.Clone()));
        }

        g.Stock.AddRange(Stock.Select(c => c.Clone()));
        g.Foundations.AddRange(Foundations);
        return g;
    }
}
