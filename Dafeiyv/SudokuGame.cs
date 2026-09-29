using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dafeiyv
{
    public enum SudokuDifficulty { Easy, Medium, Hard }

    /// <summary>数独游戏引擎：生成唯一解题目、校验输入、提示、检查错误</summary>
    public class SudokuGame
    {
        private readonly Random _rand = new Random();

        public int[,] Solution { get; private set; } = new int[9, 9];   // 完整解
        public int[,] Board { get; private set; } = new int[9, 9];       // 当前玩家棋盘（0=空）
        public bool[,] Given { get; private set; } = new bool[9, 9];     // 初始固定格
        public bool[,,] Marks { get; private set; } = new bool[9, 9, 10]; // 备注（铅笔小数字），[行,列,数字1-9]
        public int Mistakes { get; private set; }                       // 填错次数
        public SudokuDifficulty Difficulty { get; private set; } = SudokuDifficulty.Medium;

        public void NewGame(SudokuDifficulty difficulty)
        {
            Difficulty = difficulty;

            // 1) 随机生成一个完整解
            var solution = new int[9, 9];
            FillGrid(solution, 0, 0);
            Solution = solution;

            // 2) 挖空（保持唯一解）
            int target = difficulty switch
            {
                SudokuDifficulty.Easy => 36,
                SudokuDifficulty.Hard => 54,
                _ => 45
            };
            var puzzle = (int[,])solution.Clone();
            var order = Enumerable.Range(0, 81).OrderBy(_ => _rand.Next()).ToList();
            int removed = 0;
            foreach (var idx in order)
            {
                if (removed >= target) break;
                int r = idx / 9, c = idx % 9;
                int backup = puzzle[r, c];
                puzzle[r, c] = 0;
                if (CountSolutions(puzzle) != 1)
                    puzzle[r, c] = backup;   // 解不唯一则放回
                else
                    removed++;
            }

            Board = (int[,])puzzle.Clone();
            Given = new bool[9, 9];
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    Given[r, c] = puzzle[r, c] != 0;
            Marks = new bool[9, 9, 10];
            Mistakes = 0;
        }

        /// <summary>玩家输入：num=0 清除。固定格不可改。允许填入任何数字（包括与已有数字冲突的），
        /// 是否填错由「检查」功能判定；与解不同计一次错误。填入真实数字会清掉该格备注。</summary>
        public bool TrySet(int row, int col, int num)
        {
            if (row < 0 || row > 8 || col < 0 || col > 8) return false;
            if (Given[row, col]) return false;
            if (num < 0 || num > 9) return false;

            if (num == 0) { Board[row, col] = 0; return true; }   // 清除数字时保留备注

            if (num != Solution[row, col]) Mistakes++;
            Board[row, col] = num;
            ClearMarks(row, col);   // 正式填数覆盖备注
            return true;
        }

        /// <summary>右键数字切换该格备注（铅笔小数字）。固定格/已填数字的格子不能备注。</summary>
        public bool ToggleMark(int row, int col, int num)
        {
            if (row < 0 || row > 8 || col < 0 || col > 8) return false;
            if (Given[row, col] || Board[row, col] != 0) return false;
            if (num < 1 || num > 9) return false;
            Marks[row, col, num] = !Marks[row, col, num];
            return true;
        }

        /// <summary>清掉某格所有备注</summary>
        public void ClearMarks(int row, int col)
        {
            for (int n = 1; n <= 9; n++) Marks[row, col, n] = false;
        }

        public bool IsSolved() => IsFull() && GetErrors().Count == 0;

        public bool IsFull()
        {
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (Board[r, c] == 0) return false;
            return true;
        }

        /// <summary>与解不一致的格子</summary>
        public List<(int r, int c)> GetErrors()
        {
            var list = new List<(int, int)>();
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (Board[r, c] != 0 && Board[r, c] != Solution[r, c])
                        list.Add((r, c));
            return list;
        }

        public int FilledCount()
        {
            int n = 0;
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (Board[r, c] != 0) n++;
            return n;
        }

        /// <summary>提示：自动填第一个空格为正确数字，返回是否成功及位置</summary>
        public bool Hint(out int row, out int col)
        {
            row = -1; col = -1;
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (Board[r, c] == 0)
                    {
                        Board[r, c] = Solution[r, c];
                        ClearMarks(r, c);   // 提示填数同样覆盖备注
                        row = r; col = c;
                        return true;
                    }
            return false;
        }

        /// <summary>回到出题状态（清除玩家填写与备注）</summary>
        public void Reset()
        {
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (!Given[r, c]) Board[r, c] = 0;
            Marks = new bool[9, 9, 10];
            Mistakes = 0;
        }

        /// <summary>棋盘文本（供 AI 分析用）：每行 9 位数字，0 表示空格</summary>
        public string ToBoardText()
        {
            var sb = new StringBuilder();
            for (int r = 0; r < 9; r++)
            {
                sb.Append("行").Append(r + 1).Append(": ");
                for (int c = 0; c < 9; c++) sb.Append(Board[r, c]);
                sb.AppendLine();
            }
            sb.Append("错误次数: ").Append(Mistakes).Append("，已填: ").Append(FilledCount()).Append("/81");
            return sb.ToString();
        }

        // ---------- 生成辅助 ----------

        private bool FillGrid(int[,] g, int r, int c)
        {
            if (r == 9) return true;
            int nr = c == 8 ? r + 1 : r;
            int nc = c == 8 ? 0 : c + 1;
            var nums = Enumerable.Range(1, 9).OrderBy(_ => _rand.Next()).ToList();
            foreach (var n in nums)
            {
                if (Ok(g, r, c, n))
                {
                    g[r, c] = n;
                    if (FillGrid(g, nr, nc)) return true;
                    g[r, c] = 0;
                }
            }
            return false;
        }

        private static bool Ok(int[,] g, int r, int c, int n)
        {
            for (int i = 0; i < 9; i++)
            {
                if (g[r, i] == n || g[i, c] == n) return false;
            }
            int br = r / 3 * 3, bc = c / 3 * 3;
            for (int i = br; i < br + 3; i++)
                for (int j = bc; j < bc + 3; j++)
                    if (g[i, j] == n) return false;
            return true;
        }

        private int CountSolutions(int[,] g)
        {
            int count = 0;
            Count(g, ref count);
            return count;
        }

        private void Count(int[,] g, ref int count)
        {
            if (count > 1) return;
            int r = -1, c = -1;
            for (int i = 0; i < 9 && r < 0; i++)
                for (int j = 0; j < 9; j++)
                    if (g[i, j] == 0) { r = i; c = j; break; }
            if (r < 0) { count++; return; }
            for (int n = 1; n <= 9; n++)
            {
                if (Ok(g, r, c, n))
                {
                    g[r, c] = n;
                    Count(g, ref count);
                    g[r, c] = 0;
                    if (count > 1) return;
                }
            }
        }
    }
}
