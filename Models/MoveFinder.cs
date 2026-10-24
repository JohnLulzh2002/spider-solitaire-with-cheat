namespace SpiderSolitaireWithCheat.Models;

/// <summary>一个建议走法：把 FromCol 列 Index 起的序列移动到 ToCol。</summary>
public sealed record Suggestion(int FromCol, int Index, int ToCol, double Score);

/// <summary>
/// 走法搜索：为「提示」与双击自动移动挑选最优走法。
/// 评分优先级：能完成序列 > 同花续接 > 翻开暗牌 > 其他。
/// </summary>
public static class MoveFinder
{
    public static Suggestion? FindBest(GameState s) => Search(s, null);

    public static Suggestion? FindTargetFor(GameState s, int fromCol, int index) => Search(s, (fromCol, index));

    /// <summary>返回当前所有可行走法，按评分从高到低排序（供提示轮换）。</summary>
    public static List<Suggestion> FindAll(GameState s)
    {
        var all = new List<Suggestion>();
        for (int from = 0; from < GameState.ColumnCount; from++)
        {
            var pile = s.Tableau[from];
            for (int idx = 0; idx < pile.Count; idx++)
            {
                if (!s.CanPickUp(from, idx))
                {
                    continue;
                }

                for (int to = 0; to < GameState.ColumnCount; to++)
                {
                    if (to == from || !s.CanDrop(from, idx, to))
                    {
                        continue;
                    }

                    all.Add(new Suggestion(from, idx, to, Evaluate(s, from, idx, to)));
                }
            }
        }

        return all.OrderByDescending(x => x.Score).ToList();
    }

    private static Suggestion? Search(GameState s, (int Col, int Index)? only)
    {
        Suggestion? best = null;
        double bestScore = double.NegativeInfinity;
        for (int from = 0; from < GameState.ColumnCount; from++)
        {
            if (only is { } o && from != o.Col)
            {
                continue;
            }

            var pile = s.Tableau[from];
            for (int idx = 0; idx < pile.Count; idx++)
            {
                if (only is { } o2 && idx != o2.Index)
                {
                    continue;
                }

                if (!s.CanPickUp(from, idx))
                {
                    continue;
                }

                for (int to = 0; to < GameState.ColumnCount; to++)
                {
                    if (to == from || !s.CanDrop(from, idx, to))
                    {
                        continue;
                    }

                    double sc = Evaluate(s, from, idx, to);
                    if (sc > bestScore)
                    {
                        bestScore = sc;
                        best = new Suggestion(from, idx, to, sc);
                    }
                }
            }
        }

        return best;
    }

    public static double Evaluate(GameState s, int from, int idx, int to)
    {
        if (s.WouldComplete(from, idx, to))
        {
            return 1000;
        }

        var pileFrom = s.Tableau[from];
        bool exposes = idx > 0 && !pileFrom[idx - 1].FaceUp;
        Card? below = idx > 0 ? pileFrom[idx - 1] : null;
        var run0 = pileFrom[idx];
        bool breaksSuit = below is { FaceUp: true } b &&
                          b.Suit == run0.Suit && b.Rank == run0.Rank + 1;
        var target = s.Tableau[to];

        double sc = 0;
        if (target.Count == 0)
        {
            // 移到空列：把 K 挪进空列有价值，其他牌一般不值
            sc += run0.Rank == 13 ? 18 : -6;
            if (exposes)
            {
                sc += 22;
            }

            if (breaksSuit && !exposes)
            {
                sc -= 45;
            }
        }
        else
        {
            bool sameSuit = target[^1].Suit == run0.Suit;
            sc += sameSuit ? 55 + (pileFrom.Count - idx) * 2 : exposes ? 32 : 9;
            if (exposes)
            {
                sc += 45;
            }

            if (breaksSuit)
            {
                sc -= exposes ? 8 : 55; // 拆散已有的同花接续
            }
        }

        if (idx == 0)
        {
            sc += 6; // 移空一列
        }

        return sc;
    }
}
