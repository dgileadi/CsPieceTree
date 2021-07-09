/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PieceTree
{

    public class PieceTreeTests
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ\r\n";

        static char RandomChar() {
            return alphabet[RandomInt(alphabet.Length)];
        }

        static int RandomInt(int bound) {
            return random.Next(bound);
        }
        private static Random random = new Random();

        static string[] SplitLines(string str) {
            return str.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }

        static string RandomStr(int len = 10) {
            var results = new StringBuilder();
            for (int i = 0; i < len; i++)
                results.Append(RandomChar());
            return results.ToString();
        }

        static string TrimLineFeed(string text) {
            if (text.Length == 0) {
                return text;
            }

            return text.TrimEnd('\r', '\n');
        }

        //#region Assertion

        static void TestLinesContent(string str, PieceTree pieceTable) {
            var lines = SplitLines(str);
            Assert.AreEqual(lines.Length, pieceTable.LineCount);
            Assert.AreEqual(str, pieceTable.GetLinesRawContent());
            for (var i = 0; i < lines.Length; i++) {
                Assert.AreEqual(lines[i], pieceTable.GetLineContent(i + 1));
                Assert.AreEqual(
                    TrimLineFeed(
                        pieceTable.GetValueInRange(
                            new TextPosition(
                                i + 1,
                                1),
                            new TextPosition(
                                i + 1,
                                lines[i].Length + (i == lines.Length - 1 ? 1 : 2)
                            )
                        )
                    ),
                    lines[i]
                );
            }
        }

        static void TestLineStarts(string str, PieceTree pieceTable) {
            var lineStarts = new List<int>() { 0 };

            // Reset regex to search from the beginning
            var _regex = new Regex(@"\r\n|\r|\n");
            var prevMatchStartIndex = -1;
            var prevMatchLength = 0;

            var matches = _regex.Matches(str);
            foreach (Match match in matches)
            {
                if (prevMatchStartIndex + prevMatchLength == str.Length) {
                    // Reached the end of the line
                    break;
                }

                var matchStartIndex = match.Index;
                var matchLength = match.Length;

                if (
                    matchStartIndex == prevMatchStartIndex &&
                    matchLength == prevMatchLength
                ) {
                    // Exit early if the regex matches the same range twice
                    break;
                }

                prevMatchStartIndex = matchStartIndex;
                prevMatchLength = matchLength;

                lineStarts.Add(matchStartIndex + matchLength);
            }

            for (var i = 0; i < lineStarts.Count; i++) {
                Assert.AreEqual(
                    pieceTable.GetPositionAt(lineStarts[i]),
                    new TextPosition(i + 1, 1)
                );
                Assert.AreEqual(pieceTable.GetOffsetAt(i + 1, 1), lineStarts[i]);
            }

            for (var i = 1; i < lineStarts.Count; i++) {
                var pos = pieceTable.GetPositionAt(lineStarts[i] - 1);
                Assert.AreEqual(
                    pieceTable.GetOffsetAt(pos.line, pos.column),
                    lineStarts[i] - 1
                );
            }
        }

        static PieceTree CreateTextBuffer(params string[] val)
        {
            return CreateTextBuffer(val, true);
        }

        static PieceTree CreateNonNormalizedTextBuffer(params string[] val)
        {
            return CreateTextBuffer(val, false);
        }

        static PieceTree CreateTextBuffer(string[] val, bool normalizeEOL = true) {
            return new PieceTree(val, "\n", normalizeEOL);
        }

        static void AssertTreeInvariants(PieceTree T) {
            Assert.AreEqual(NodeColor.Black, TreeNode.SENTINEL.color);
            Assert.AreEqual(TreeNode.SENTINEL, TreeNode.SENTINEL.parent);
            Assert.AreEqual(TreeNode.SENTINEL, TreeNode.SENTINEL.left);
            Assert.AreEqual(TreeNode.SENTINEL, TreeNode.SENTINEL.right);
            Assert.AreEqual(0, TreeNode.SENTINEL.size_left);
            Assert.AreEqual(0, TreeNode.SENTINEL.lf_left);
            AssertValidTree(T);
        }

        static int Depth(TreeNode n) {
            if (n == TreeNode.SENTINEL) {
                // The leafs are black
                return 1;
            }
            Assert.AreEqual(Depth(n.right), Depth(n.left));
            return (n.color == NodeColor.Black ? 1 : 0) + Depth(n.left);
        }

        struct NodeValidity
        {
            public readonly int size;
            public readonly int lf_cnt;

            public NodeValidity(int size, int lf_cnt)
            {
                this.size = size;
                this.lf_cnt = lf_cnt;
            }
        }

        static NodeValidity AssertValidNode(TreeNode n) {
            if (n == TreeNode.SENTINEL) {
                return new NodeValidity(size: 0, lf_cnt: 0);
            }

            var l = n.left;
            var r = n.right;

            if (n.color == NodeColor.Red) {
                Assert.AreEqual(NodeColor.Black, l.color);
                Assert.AreEqual(NodeColor.Black, r.color);
            }

            var actualLeft = AssertValidNode(l);
            Assert.AreEqual(n.lf_left, actualLeft.lf_cnt);
            Assert.AreEqual(n.size_left, actualLeft.size);
            var actualRight = AssertValidNode(r);

            return new NodeValidity(size: n.size_left + n.length + actualRight.size, lf_cnt: n.lf_left + n.lineFeedCnt + actualRight.lf_cnt);
        }

        static void AssertValidTree(PieceTree T) {
            if (T.root == TreeNode.SENTINEL) {
                return;
            }
            Assert.AreEqual(NodeColor.Black, T.root.color);
            Assert.AreEqual(Depth(T.root.right), Depth(T.root.left));
            AssertValidNode(T.root);
        }

        //#endregion

        public class InsertsAndDeletes
        {
            [Test]
            public void BasicInsertDelete()
            {
                var pieceTable = CreateTextBuffer(
                    "This is a document with some text."
                );

                pieceTable.Insert(34, "This is some more text to insert at offset 34.");
                Assert.AreEqual(
                    "This is a document with some text.This is some more text to insert at offset 34.",
                    pieceTable.GetLinesRawContent()
                );
                pieceTable.Delete(42, 5);
                Assert.AreEqual(
                    "This is a document with some text.This is more text to insert at offset 34.",
                    pieceTable.GetLinesRawContent()
                );
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void MoreInserts()
            {
                var pt = CreateTextBuffer("");

                pt.Insert(0, "AAA");
                Assert.AreEqual("AAA", pt.GetLinesRawContent());
                pt.Insert(0, "BBB");
                Assert.AreEqual("BBBAAA", pt.GetLinesRawContent());
                pt.Insert(6, "CCC");
                Assert.AreEqual("BBBAAACCC", pt.GetLinesRawContent());
                pt.Insert(5, "DDD");
                Assert.AreEqual("BBBAADDDACCC", pt.GetLinesRawContent());
                AssertTreeInvariants(pt);
            }

            [Test]
            public void MoreDeletes()
            {
                var pt = CreateTextBuffer("012345678");
                pt.Delete(8, 1);
                Assert.AreEqual("01234567", pt.GetLinesRawContent());
                pt.Delete(0, 1);
                Assert.AreEqual("1234567", pt.GetLinesRawContent());
                pt.Delete(5, 1);
                Assert.AreEqual("123457", pt.GetLinesRawContent());
                pt.Delete(5, 1);
                Assert.AreEqual("12345", pt.GetLinesRawContent());
                pt.Delete(0, 5);
                Assert.AreEqual("", pt.GetLinesRawContent());
                AssertTreeInvariants(pt);
            }

            [Test]
            public void RandomTest_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "ceLPHmFzvCtFeHkCBej ");
                str = str.Substring(0, 0) + "ceLPHmFzvCtFeHkCBej " + str.Substring(0);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                pieceTable.Insert(8, "gDCEfNYiBUNkSwtvB K ");
                str = str.Substring(0, 8) + "gDCEfNYiBUNkSwtvB K " + str.Substring(8);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                pieceTable.Insert(38, "cyNcHxjNPPoehBJldLS ");
                str = str.Substring(0, 38) + "cyNcHxjNPPoehBJldLS " + str.Substring(38);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                pieceTable.Insert(59, "ejMx\nOTgWlbpeDExjOk ");
                str = str.Substring(0, 59) + "ejMx\nOTgWlbpeDExjOk " + str.Substring(59);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTest_2()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "VgPG ");
                str = str.Substring(0, 0) + "VgPG " + str.Substring(0);
                pieceTable.Insert(2, "DdWF ");
                str = str.Substring(0, 2) + "DdWF " + str.Substring(2);
                pieceTable.Insert(0, "hUJc ");
                str = str.Substring(0, 0) + "hUJc " + str.Substring(0);
                pieceTable.Insert(8, "lQEq ");
                str = str.Substring(0, 8) + "lQEq " + str.Substring(8);
                pieceTable.Insert(10, "Gbtp ");
                str = str.Substring(0, 10) + "Gbtp " + str.Substring(10);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTest_3()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "gYSz");
                str = str.Substring(0, 0) + "gYSz" + str.Substring(0);
                pieceTable.Insert(1, "mDQe");
                str = str.Substring(0, 1) + "mDQe" + str.Substring(1);
                pieceTable.Insert(1, "DTMQ");
                str = str.Substring(0, 1) + "DTMQ" + str.Substring(1);
                pieceTable.Insert(2, "GGZB");
                str = str.Substring(0, 2) + "GGZB" + str.Substring(2);
                pieceTable.Insert(12, "wXpq");
                str = str.Substring(0, 12) + "wXpq" + str.Substring(12);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
            }

            [Test]
            public void RandomDelete_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");

                pieceTable.Insert(0, "vfb");
                str = str.Substring(0, 0) + "vfb" + str.Substring(0);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                pieceTable.Insert(0, "zRq");
                str = str.Substring(0, 0) + "zRq" + str.Substring(0);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                pieceTable.Delete(5, 1);
                str = str.Substring(0, 5) + str.Substring(5 + 1);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                pieceTable.Insert(1, "UNw");
                str = str.Substring(0, 1) + "UNw" + str.Substring(1);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                pieceTable.Delete(4, 3);
                str = str.Substring(0, 4) + str.Substring(4 + 3);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                pieceTable.Delete(1, 4);
                str = str.Substring(0, 1) + str.Substring(1 + 4);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                pieceTable.Delete(0, 1);
                str = str.Substring(0, 0) + str.Substring(0 + 1);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomDelete_2()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");

                pieceTable.Insert(0, "IDT");
                str = str.Substring(0, 0) + "IDT" + str.Substring(0);
                pieceTable.Insert(3, "wwA");
                str = str.Substring(0, 3) + "wwA" + str.Substring(3);
                pieceTable.Insert(3, "Gnr");
                str = str.Substring(0, 3) + "Gnr" + str.Substring(3);
                pieceTable.Delete(6, 3);
                str = str.Substring(0, 6) + str.Substring(6 + 3);
                pieceTable.Insert(4, "eHp");
                str = str.Substring(0, 4) + "eHp" + str.Substring(4);
                pieceTable.Insert(1, "UAi");
                str = str.Substring(0, 1) + "UAi" + str.Substring(1);
                pieceTable.Insert(2, "FrR");
                str = str.Substring(0, 2) + "FrR" + str.Substring(2);
                pieceTable.Delete(6, 7);
                str = str.Substring(0, 6) + str.Substring(6 + 7);
                pieceTable.Delete(3, 5);
                str = str.Substring(0, 3) + str.Substring(3 + 5);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomDelete_3()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "PqM");
                str = str.Substring(0, 0) + "PqM" + str.Substring(0);
                pieceTable.Delete(1, 2);
                str = str.Substring(0, 1) + str.Substring(1 + 2);
                pieceTable.Insert(1, "zLc");
                str = str.Substring(0, 1) + "zLc" + str.Substring(1);
                pieceTable.Insert(0, "MEX");
                str = str.Substring(0, 0) + "MEX" + str.Substring(0);
                pieceTable.Insert(0, "jZh");
                str = str.Substring(0, 0) + "jZh" + str.Substring(0);
                pieceTable.Insert(8, "GwQ");
                str = str.Substring(0, 8) + "GwQ" + str.Substring(8);
                pieceTable.Delete(5, 6);
                str = str.Substring(0, 5) + str.Substring(5 + 6);
                pieceTable.Insert(4, "ktw");
                str = str.Substring(0, 4) + "ktw" + str.Substring(4);
                pieceTable.Insert(5, "GVu");
                str = str.Substring(0, 5) + "GVu" + str.Substring(5);
                pieceTable.Insert(9, "jdm");
                str = str.Substring(0, 9) + "jdm" + str.Substring(9);
                pieceTable.Insert(15, "na\n");
                str = str.Substring(0, 15) + "na\n" + str.Substring(15);
                pieceTable.Delete(5, 8);
                str = str.Substring(0, 5) + str.Substring(5 + 8);
                pieceTable.Delete(3, 4);
                str = str.Substring(0, 3) + str.Substring(3 + 4);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteRBug_1()
            {
                var str = "a";
                var pieceTable = CreateTextBuffer("a");
                pieceTable.Delete(0, 1);
                str = str.Substring(0, 0) + str.Substring(0 + 1);
                pieceTable.Insert(0, "\r\r\n\n");
                str = str.Substring(0, 0) + "\r\r\n\n" + str.Substring(0);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Insert(2, "\n\n\ra");
                str = str.Substring(0, 2) + "\n\n\ra" + str.Substring(2);
                pieceTable.Delete(4, 3);
                str = str.Substring(0, 4) + str.Substring(4 + 3);
                pieceTable.Insert(2, "\na\r\r");
                str = str.Substring(0, 2) + "\na\r\r" + str.Substring(2);
                pieceTable.Insert(6, "\ra\n\n");
                str = str.Substring(0, 6) + "\ra\n\n" + str.Substring(6);
                pieceTable.Insert(0, "aa\n\n");
                str = str.Substring(0, 0) + "aa\n\n" + str.Substring(0);
                pieceTable.Insert(5, "\n\na\r");
                str = str.Substring(0, 5) + "\n\na\r" + str.Substring(5);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteRBug_2()
            {
                var str = "a";
                var pieceTable = CreateTextBuffer("a");
                pieceTable.Insert(1, "\naa\r");
                str = str.Substring(0, 1) + "\naa\r" + str.Substring(1);
                pieceTable.Delete(0, 4);
                str = str.Substring(0, 0) + str.Substring(0 + 4);
                pieceTable.Insert(1, "\r\r\na");
                str = str.Substring(0, 1) + "\r\r\na" + str.Substring(1);
                pieceTable.Insert(2, "\n\r\ra");
                str = str.Substring(0, 2) + "\n\r\ra" + str.Substring(2);
                pieceTable.Delete(4, 1);
                str = str.Substring(0, 4) + str.Substring(4 + 1);
                pieceTable.Insert(8, "\r\n\r\r");
                str = str.Substring(0, 8) + "\r\n\r\r" + str.Substring(8);
                pieceTable.Insert(7, "\n\n\na");
                str = str.Substring(0, 7) + "\n\n\na" + str.Substring(7);
                pieceTable.Insert(13, "a\n\na");
                str = str.Substring(0, 13) + "a\n\na" + str.Substring(13);
                pieceTable.Delete(17, 3);
                str = str.Substring(0, 17) + str.Substring(17 + 3);
                pieceTable.Insert(2, "a\ra\n");
                str = str.Substring(0, 2) + "a\ra\n" + str.Substring(2);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteRBug_3()
            {
                var str = "a";
                var pieceTable = CreateTextBuffer("a");
                pieceTable.Insert(0, "\r\na\r");
                str = str.Substring(0, 0) + "\r\na\r" + str.Substring(0);
                pieceTable.Delete(2, 3);
                str = str.Substring(0, 2) + str.Substring(2 + 3);
                pieceTable.Insert(2, "a\r\n\r");
                str = str.Substring(0, 2) + "a\r\n\r" + str.Substring(2);
                pieceTable.Delete(4, 2);
                str = str.Substring(0, 4) + str.Substring(4 + 2);
                pieceTable.Insert(4, "a\n\r\n");
                str = str.Substring(0, 4) + "a\n\r\n" + str.Substring(4);
                pieceTable.Insert(1, "aa\n\r");
                str = str.Substring(0, 1) + "aa\n\r" + str.Substring(1);
                pieceTable.Insert(7, "\na\r\n");
                str = str.Substring(0, 7) + "\na\r\n" + str.Substring(7);
                pieceTable.Insert(5, "\n\na\r");
                str = str.Substring(0, 5) + "\n\na\r" + str.Substring(5);
                pieceTable.Insert(10, "\r\r\n\r");
                str = str.Substring(0, 10) + "\r\r\n\r" + str.Substring(10);
                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                pieceTable.Delete(21, 3);
                str = str.Substring(0, 21) + str.Substring(21 + 3);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteRBug_4s()
            {
                var str = "a";
                var pieceTable = CreateTextBuffer("a");
                pieceTable.Delete(0, 1);
                str = str.Substring(0, 0) + str.Substring(0 + 1);
                pieceTable.Insert(0, "\naaa");
                str = str.Substring(0, 0) + "\naaa" + str.Substring(0);
                pieceTable.Insert(2, "\n\naa");
                str = str.Substring(0, 2) + "\n\naa" + str.Substring(2);
                pieceTable.Delete(1, 4);
                str = str.Substring(0, 1) + str.Substring(1 + 4);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Delete(1, 2);
                str = str.Substring(0, 1) + str.Substring(1 + 2);
                pieceTable.Delete(0, 1);
                str = str.Substring(0, 0) + str.Substring(0 + 1);
                pieceTable.Insert(0, "a\n\n\r");
                str = str.Substring(0, 0) + "a\n\n\r" + str.Substring(0);
                pieceTable.Insert(2, "aa\r\n");
                str = str.Substring(0, 2) + "aa\r\n" + str.Substring(2);
                pieceTable.Insert(3, "a\naa");
                str = str.Substring(0, 3) + "a\naa" + str.Substring(3);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteRBug_5()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "\n\n\n\r");
                str = str.Substring(0, 0) + "\n\n\n\r" + str.Substring(0);
                pieceTable.Insert(1, "\n\n\n\r");
                str = str.Substring(0, 1) + "\n\n\n\r" + str.Substring(1);
                pieceTable.Insert(2, "\n\r\r\r");
                str = str.Substring(0, 2) + "\n\r\r\r" + str.Substring(2);
                pieceTable.Insert(8, "\n\r\n\r");
                str = str.Substring(0, 8) + "\n\r\n\r" + str.Substring(8);
                pieceTable.Delete(5, 2);
                str = str.Substring(0, 5) + str.Substring(5 + 2);
                pieceTable.Insert(4, "\n\r\r\r");
                str = str.Substring(0, 4) + "\n\r\r\r" + str.Substring(4);
                pieceTable.Insert(8, "\n\n\n\r");
                str = str.Substring(0, 8) + "\n\n\n\r" + str.Substring(8);
                pieceTable.Delete(0, 7);
                str = str.Substring(0, 0) + str.Substring(0 + 7);
                pieceTable.Insert(1, "\r\n\r\r");
                str = str.Substring(0, 1) + "\r\n\r\r" + str.Substring(1);
                pieceTable.Insert(15, "\n\r\r\r");
                str = str.Substring(0, 15) + "\n\r\r\r" + str.Substring(15);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                AssertTreeInvariants(pieceTable);
            }
        }

        public class PrefixSumForLineFeed
        {
            [Test]
            public void Basic()
            {
                var pieceTable = CreateTextBuffer("1\n2\n3\n4");

                Assert.AreEqual(4, pieceTable.LineCount);
                Assert.AreEqual(new TextPosition(1, 1), pieceTable.GetPositionAt(0));
                Assert.AreEqual(new TextPosition(1, 2), pieceTable.GetPositionAt(1));
                Assert.AreEqual(new TextPosition(2, 1), pieceTable.GetPositionAt(2));
                Assert.AreEqual(new TextPosition(2, 2), pieceTable.GetPositionAt(3));
                Assert.AreEqual(new TextPosition(3, 1), pieceTable.GetPositionAt(4));
                Assert.AreEqual(new TextPosition(3, 2), pieceTable.GetPositionAt(5));
                Assert.AreEqual(new TextPosition(4, 1), pieceTable.GetPositionAt(6));

                Assert.AreEqual(0, pieceTable.GetOffsetAt(1, 1));
                Assert.AreEqual(1, pieceTable.GetOffsetAt(1, 2));
                Assert.AreEqual(2, pieceTable.GetOffsetAt(2, 1));
                Assert.AreEqual(3, pieceTable.GetOffsetAt(2, 2));
                Assert.AreEqual(4, pieceTable.GetOffsetAt(3, 1));
                Assert.AreEqual(5, pieceTable.GetOffsetAt(3, 2));
                Assert.AreEqual(6, pieceTable.GetOffsetAt(4, 1));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void Append()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\nde");
                pieceTable.Insert(8, "fh\ni\njk");

                Assert.AreEqual(6, pieceTable.LineCount);
                Assert.AreEqual(new TextPosition(4, 4), pieceTable.GetPositionAt(9));
                Assert.AreEqual(0, pieceTable.GetOffsetAt(1, 1));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void Insert()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\nde");
                pieceTable.Insert(7, "fh\ni\njk");

                Assert.AreEqual(6, pieceTable.LineCount);
                Assert.AreEqual(new TextPosition(4, 1), pieceTable.GetPositionAt(6));
                Assert.AreEqual(new TextPosition(4, 2), pieceTable.GetPositionAt(7));
                Assert.AreEqual(new TextPosition(4, 3), pieceTable.GetPositionAt(8));
                Assert.AreEqual(new TextPosition(4, 4), pieceTable.GetPositionAt(9));
                Assert.AreEqual(new TextPosition(6, 1), pieceTable.GetPositionAt(12));
                Assert.AreEqual(new TextPosition(6, 2), pieceTable.GetPositionAt(13));
                Assert.AreEqual(new TextPosition(6, 3), pieceTable.GetPositionAt(14));

                Assert.AreEqual(6, pieceTable.GetOffsetAt(4, 1));
                Assert.AreEqual(7, pieceTable.GetOffsetAt(4, 2));
                Assert.AreEqual(8, pieceTable.GetOffsetAt(4, 3));
                Assert.AreEqual(9, pieceTable.GetOffsetAt(4, 4));
                Assert.AreEqual(12, pieceTable.GetOffsetAt(6, 1));
                Assert.AreEqual(13, pieceTable.GetOffsetAt(6, 2));
                Assert.AreEqual(14, pieceTable.GetOffsetAt(6, 3));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void Delete()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\ndefh\ni\njk");
                pieceTable.Delete(7, 2);

                Assert.AreEqual("a\nb\nc\ndh\ni\njk", pieceTable.GetLinesRawContent());
                Assert.AreEqual(6, pieceTable.LineCount);
                Assert.AreEqual(new TextPosition(4, 1), pieceTable.GetPositionAt(6));
                Assert.AreEqual(new TextPosition(4, 2), pieceTable.GetPositionAt(7));
                Assert.AreEqual(new TextPosition(4, 3), pieceTable.GetPositionAt(8));
                Assert.AreEqual(new TextPosition(5, 1), pieceTable.GetPositionAt(9));
                Assert.AreEqual(new TextPosition(6, 1), pieceTable.GetPositionAt(11));
                Assert.AreEqual(new TextPosition(6, 2), pieceTable.GetPositionAt(12));
                Assert.AreEqual(new TextPosition(6, 3), pieceTable.GetPositionAt(13));

                Assert.AreEqual(6, pieceTable.GetOffsetAt(4, 1));
                Assert.AreEqual(7, pieceTable.GetOffsetAt(4, 2));
                Assert.AreEqual(8, pieceTable.GetOffsetAt(4, 3));
                Assert.AreEqual(9, pieceTable.GetOffsetAt(5, 1));
                Assert.AreEqual(11, pieceTable.GetOffsetAt(6, 1));
                Assert.AreEqual(12, pieceTable.GetOffsetAt(6, 2));
                Assert.AreEqual(13, pieceTable.GetOffsetAt(6, 3));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void AddDelete_1()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\nde");
                pieceTable.Insert(8, "fh\ni\njk");
                pieceTable.Delete(7, 2);

                Assert.AreEqual("a\nb\nc\ndh\ni\njk", pieceTable.GetLinesRawContent());
                Assert.AreEqual(6, pieceTable.LineCount);
                Assert.AreEqual(new TextPosition(4, 1), pieceTable.GetPositionAt(6));
                Assert.AreEqual(new TextPosition(4, 2), pieceTable.GetPositionAt(7));
                Assert.AreEqual(new TextPosition(4, 3), pieceTable.GetPositionAt(8));
                Assert.AreEqual(new TextPosition(5, 1), pieceTable.GetPositionAt(9));
                Assert.AreEqual(new TextPosition(6, 1), pieceTable.GetPositionAt(11));
                Assert.AreEqual(new TextPosition(6, 2), pieceTable.GetPositionAt(12));
                Assert.AreEqual(new TextPosition(6, 3), pieceTable.GetPositionAt(13));

                Assert.AreEqual(6, pieceTable.GetOffsetAt(4, 1));
                Assert.AreEqual(7, pieceTable.GetOffsetAt(4, 2));
                Assert.AreEqual(8, pieceTable.GetOffsetAt(4, 3));
                Assert.AreEqual(9, pieceTable.GetOffsetAt(5, 1));
                Assert.AreEqual(11, pieceTable.GetOffsetAt(6, 1));
                Assert.AreEqual(12, pieceTable.GetOffsetAt(6, 2));
                Assert.AreEqual(13, pieceTable.GetOffsetAt(6, 3));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void InsertRandomBug_1PrefixSumComputerRemoveValuesStartCntCntIs_1Based()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, " ZX \n Z\nZ\n YZ\nY\nZXX ");
                str =
                    str.Substring(0, 0) +
                    " ZX \n Z\nZ\n YZ\nY\nZXX " +
                    str.Substring(0);
                pieceTable.Insert(14, "X ZZ\nYZZYZXXY Y XY\n ");
                str =
                    str.Substring(0, 14) + "X ZZ\nYZZYZXXY Y XY\n " + str.Substring(14);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void InsertRandomBug_2PrefixSumComputerInitializeDoesNotDoDeepCopyOfUInt32Array()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "ZYZ\nYY XY\nX \nZ Y \nZ ");
                str =
                    str.Substring(0, 0) + "ZYZ\nYY XY\nX \nZ Y \nZ " + str.Substring(0);
                pieceTable.Insert(3, "XXY \n\nY Y YYY  ZYXY ");
                str = str.Substring(0, 3) + "XXY \n\nY Y YYY  ZYXY " + str.Substring(3);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteRandomBug_1IForgotToUpdateTheLineFeedCntWhenDeletionIsOnOneSinglePiece()
            {
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "ba\na\nca\nba\ncbab\ncaa ");
                pieceTable.Insert(13, "cca\naabb\ncac\nccc\nab ");
                pieceTable.Delete(5, 8);
                pieceTable.Delete(30, 2);
                pieceTable.Insert(24, "cbbacccbac\nbaaab\n\nc ");
                pieceTable.Delete(29, 3);
                pieceTable.Delete(23, 9);
                pieceTable.Delete(21, 5);
                pieceTable.Delete(30, 3);
                pieceTable.Insert(3, "cb\nac\nc\n\nacc\nbb\nb\nc ");
                pieceTable.Delete(19, 5);
                pieceTable.Insert(18, "\nbb\n\nacbc\ncbb\nc\nbb\n ");
                pieceTable.Insert(65, "cbccbac\nbc\n\nccabba\n ");
                pieceTable.Insert(77, "a\ncacb\n\nac\n\n\n\n\nabab ");
                pieceTable.Delete(30, 9);
                pieceTable.Insert(45, "b\n\nc\nba\n\nbbbba\n\naa\n ");
                pieceTable.Insert(82, "ab\nbb\ncabacab\ncbc\na ");
                pieceTable.Delete(123, 9);
                pieceTable.Delete(71, 2);
                pieceTable.Insert(33, "acaa\nacb\n\naa\n\nc\n\n\n\n ");

                var str = pieceTable.GetLinesRawContent();
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteRandomBugRbTree_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer(str);
                pieceTable.Insert(0, "YXXZ\n\nYY\n");
                str = str.Substring(0, 0) + "YXXZ\n\nYY\n" + str.Substring(0);
                pieceTable.Delete(0, 5);
                str = str.Substring(0, 0) + str.Substring(0 + 5);
                pieceTable.Insert(0, "ZXYY\nX\nZ\n");
                str = str.Substring(0, 0) + "ZXYY\nX\nZ\n" + str.Substring(0);
                pieceTable.Insert(10, "\nXY\nYXYXY");
                str = str.Substring(0, 10) + "\nXY\nYXYXY" + str.Substring(10);
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteRandomBugRbTree_2()
            {
                var str = "";
                var pieceTable = CreateTextBuffer(str);
                pieceTable.Insert(0, "YXXZ\n\nYY\n");
                str = str.Substring(0, 0) + "YXXZ\n\nYY\n" + str.Substring(0);
                pieceTable.Insert(0, "ZXYY\nX\nZ\n");
                str = str.Substring(0, 0) + "ZXYY\nX\nZ\n" + str.Substring(0);
                pieceTable.Insert(10, "\nXY\nYXYXY");
                str = str.Substring(0, 10) + "\nXY\nYXYXY" + str.Substring(10);
                pieceTable.Insert(8, "YZXY\nZ\nYX");
                str = str.Substring(0, 8) + "YZXY\nZ\nYX" + str.Substring(8);
                pieceTable.Insert(12, "XX\nXXYXYZ");
                str = str.Substring(0, 12) + "XX\nXXYXYZ" + str.Substring(12);
                pieceTable.Delete(0, 4);
                str = str.Substring(0, 0) + str.Substring(0 + 4);

                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteRandomBugRbTree_3()
            {
                var str = "";
                var pieceTable = CreateTextBuffer(str);
                pieceTable.Insert(0, "YXXZ\n\nYY\n");
                str = str.Substring(0, 0) + "YXXZ\n\nYY\n" + str.Substring(0);
                pieceTable.Delete(7, 2);
                str = str.Substring(0, 7) + str.Substring(7 + 2);
                pieceTable.Delete(6, 1);
                str = str.Substring(0, 6) + str.Substring(6 + 1);
                pieceTable.Delete(0, 5);
                str = str.Substring(0, 0) + str.Substring(0 + 5);
                pieceTable.Insert(0, "ZXYY\nX\nZ\n");
                str = str.Substring(0, 0) + "ZXYY\nX\nZ\n" + str.Substring(0);
                pieceTable.Insert(10, "\nXY\nYXYXY");
                str = str.Substring(0, 10) + "\nXY\nYXYXY" + str.Substring(10);
                pieceTable.Insert(8, "YZXY\nZ\nYX");
                str = str.Substring(0, 8) + "YZXY\nZ\nYX" + str.Substring(8);
                pieceTable.Insert(12, "XX\nXXYXYZ");
                str = str.Substring(0, 12) + "XX\nXXYXYZ" + str.Substring(12);
                pieceTable.Delete(0, 4);
                str = str.Substring(0, 0) + str.Substring(0 + 4);
                pieceTable.Delete(30, 3);
                str = str.Substring(0, 30) + str.Substring(30 + 3);

                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class Offset_2Position
        {
            [Test]
            public void RandomTestsBug_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "huuyYzUfKOENwGgZLqn ");
                str = str.Substring(0, 0) + "huuyYzUfKOENwGgZLqn " + str.Substring(0);
                pieceTable.Delete(18, 2);
                str = str.Substring(0, 18) + str.Substring(18 + 2);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Delete(12, 4);
                str = str.Substring(0, 12) + str.Substring(12 + 4);
                pieceTable.Insert(3, "hMbnVEdTSdhLlPevXKF ");
                str = str.Substring(0, 3) + "hMbnVEdTSdhLlPevXKF " + str.Substring(3);
                pieceTable.Delete(22, 8);
                str = str.Substring(0, 22) + str.Substring(22 + 8);
                pieceTable.Insert(4, "S umSnYrqOmOAV\nEbZJ ");
                str = str.Substring(0, 4) + "S umSnYrqOmOAV\nEbZJ " + str.Substring(4);

                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class GetTextInRange
        {
            [Test]
            public void GetContentInRange()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\nde");
                pieceTable.Insert(8, "fh\ni\njk");
                pieceTable.Delete(7, 2);
                // "a\nb\nc\ndh\ni\njk"

                Assert.AreEqual("a\n", pieceTable.GetValueInRange(new TextPosition(1, 1), new TextPosition(1, 3)));
                Assert.AreEqual("b\n", pieceTable.GetValueInRange(new TextPosition(2, 1), new TextPosition(2, 3)));
                Assert.AreEqual("c\n", pieceTable.GetValueInRange(new TextPosition(3, 1), new TextPosition(3, 3)));
                Assert.AreEqual("dh\n", pieceTable.GetValueInRange(new TextPosition(4, 1), new TextPosition(4, 4)));
                Assert.AreEqual("i\n", pieceTable.GetValueInRange(new TextPosition(5, 1), new TextPosition(5, 3)));
                Assert.AreEqual("jk", pieceTable.GetValueInRange(new TextPosition(6, 1), new TextPosition(6, 3)));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTestValueInRange()
            {
                var str = "";
                var pieceTable = CreateTextBuffer(str);

                pieceTable.Insert(0, "ZXXY");
                str = str.Substring(0, 0) + "ZXXY" + str.Substring(0);
                pieceTable.Insert(1, "XZZY");
                str = str.Substring(0, 1) + "XZZY" + str.Substring(1);
                pieceTable.Insert(5, "\nX\n\n");
                str = str.Substring(0, 5) + "\nX\n\n" + str.Substring(5);
                pieceTable.Insert(3, "\nXX\n");
                str = str.Substring(0, 3) + "\nXX\n" + str.Substring(3);
                pieceTable.Insert(12, "YYYX");
                str = str.Substring(0, 12) + "YYYX" + str.Substring(12);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTestValueInRangeException()
            {
                var str = "";
                var pieceTable = CreateTextBuffer(str);

                pieceTable.Insert(0, "XZ\nZ");
                str = str.Substring(0, 0) + "XZ\nZ" + str.Substring(0);
                pieceTable.Delete(0, 3);
                str = str.Substring(0, 0) + str.Substring(0 + 3);
                pieceTable.Delete(0, 1);
                str = str.Substring(0, 0) + str.Substring(0 + 1);
                pieceTable.Insert(0, "ZYX\n");
                str = str.Substring(0, 0) + "ZYX\n" + str.Substring(0);
                pieceTable.Delete(0, 4);
                str = str.Substring(0, 0) + str.Substring(0 + 4);

                pieceTable.GetValueInRange(new TextPosition(1, 1), new TextPosition(1, 1));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTestsBug_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "huuyYzUfKOENwGgZLqn ");
                str = str.Substring(0, 0) + "huuyYzUfKOENwGgZLqn " + str.Substring(0);
                pieceTable.Delete(18, 2);
                str = str.Substring(0, 18) + str.Substring(18 + 2);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Delete(12, 4);
                str = str.Substring(0, 12) + str.Substring(12 + 4);
                pieceTable.Insert(3, "hMbnVEdTSdhLlPevXKF ");
                str = str.Substring(0, 3) + "hMbnVEdTSdhLlPevXKF " + str.Substring(3);
                pieceTable.Delete(22, 8);
                str = str.Substring(0, 22) + str.Substring(22 + 8);
                pieceTable.Insert(4, "S umSnYrqOmOAV\nEbZJ ");
                str = str.Substring(0, 4) + "S umSnYrqOmOAV\nEbZJ " + str.Substring(4);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomTestsBug_2()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "xfouRDZwdAHjVXJAMV\n ");
                str = str.Substring(0, 0) + "xfouRDZwdAHjVXJAMV\n " + str.Substring(0);
                pieceTable.Insert(16, "dBGndxpFZBEAIKykYYx ");
                str = str.Substring(0, 16) + "dBGndxpFZBEAIKykYYx " + str.Substring(16);
                pieceTable.Delete(7, 6);
                str = str.Substring(0, 7) + str.Substring(7 + 6);
                pieceTable.Delete(9, 7);
                str = str.Substring(0, 9) + str.Substring(9 + 7);
                pieceTable.Delete(17, 6);
                str = str.Substring(0, 17) + str.Substring(17 + 6);
                pieceTable.Delete(0, 4);
                str = str.Substring(0, 0) + str.Substring(0 + 4);
                pieceTable.Insert(9, "qvEFXCNvVkWgvykahYt ");
                str = str.Substring(0, 9) + "qvEFXCNvVkWgvykahYt " + str.Substring(9);
                pieceTable.Delete(4, 6);
                str = str.Substring(0, 4) + str.Substring(4 + 6);
                pieceTable.Insert(11, "OcSChUYT\nzPEBOpsGmR ");
                str =
                    str.Substring(0, 11) + "OcSChUYT\nzPEBOpsGmR " + str.Substring(11);
                pieceTable.Insert(15, "KJCozaXTvkE\nxnqAeTz ");
                str =
                    str.Substring(0, 15) + "KJCozaXTvkE\nxnqAeTz " + str.Substring(15);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void GetLineContent()
            {
                var pieceTable = CreateTextBuffer("1");
                Assert.AreEqual("1", pieceTable.GetLineRawContent(1));
                pieceTable.Insert(1, "2");
                Assert.AreEqual("12", pieceTable.GetLineRawContent(1));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void GetLineContentBasic()
            {
                var pieceTable = CreateTextBuffer("1\n2\n3\n4");
                Assert.AreEqual("1\n", pieceTable.GetLineRawContent(1));
                Assert.AreEqual("2\n", pieceTable.GetLineRawContent(2));
                Assert.AreEqual("3\n", pieceTable.GetLineRawContent(3));
                Assert.AreEqual("4", pieceTable.GetLineRawContent(4));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void GetLineContentAfterInsertsDeletes()
            {
                var pieceTable = CreateTextBuffer("a\nb\nc\nde");
                pieceTable.Insert(8, "fh\ni\njk");
                pieceTable.Delete(7, 2);
                // "a\nb\nc\ndh\ni\njk"

                Assert.AreEqual("a\n", pieceTable.GetLineRawContent(1));
                Assert.AreEqual("b\n", pieceTable.GetLineRawContent(2));
                Assert.AreEqual("c\n", pieceTable.GetLineRawContent(3));
                Assert.AreEqual("dh\n", pieceTable.GetLineRawContent(4));
                Assert.AreEqual("i\n", pieceTable.GetLineRawContent(5));
                Assert.AreEqual("jk", pieceTable.GetLineRawContent(6));
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void Random_1()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");

                pieceTable.Insert(0, "J eNnDzQpnlWyjmUu\ny ");
                str = str.Substring(0, 0) + "J eNnDzQpnlWyjmUu\ny " + str.Substring(0);
                pieceTable.Insert(0, "QPEeRAQmRwlJqtZSWhQ ");
                str = str.Substring(0, 0) + "QPEeRAQmRwlJqtZSWhQ " + str.Substring(0);
                pieceTable.Delete(5, 1);
                str = str.Substring(0, 5) + str.Substring(5 + 1);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void Random_2()
            {
                var str = "";
                var pieceTable = CreateTextBuffer("");
                pieceTable.Insert(0, "DZoQ tglPCRHMltejRI ");
                str = str.Substring(0, 0) + "DZoQ tglPCRHMltejRI " + str.Substring(0);
                pieceTable.Insert(10, "JRXiyYqJ qqdcmbfkKX ");
                str = str.Substring(0, 10) + "JRXiyYqJ qqdcmbfkKX " + str.Substring(10);
                pieceTable.Delete(16, 3);
                str = str.Substring(0, 16) + str.Substring(16 + 3);
                pieceTable.Delete(25, 1);
                str = str.Substring(0, 25) + str.Substring(25 + 1);
                pieceTable.Insert(18, "vH\nNlvfqQJPm\nSFkhMc ");
                str =
                    str.Substring(0, 18) + "vH\nNlvfqQJPm\nSFkhMc " + str.Substring(18);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class Crlf
        {
            [Test]
            public void DeleteCrInCrlf_1()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("");
                pieceTable.Insert(0, "a\r\nb");
                pieceTable.Delete(0, 2);

                Assert.AreEqual(2, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteCrInCrlf_2()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("");
                pieceTable.Insert(0, "a\r\nb");
                pieceTable.Delete(2, 2);

                Assert.AreEqual(2, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_1()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");
                pieceTable.Insert(0, "\n\n\r\r");
                str = str.Substring(0, 0) + "\n\n\r\r" + str.Substring(0);
                pieceTable.Insert(1, "\r\n\r\n");
                str = str.Substring(0, 1) + "\r\n\r\n" + str.Substring(1);
                pieceTable.Delete(5, 3);
                str = str.Substring(0, 5) + str.Substring(5 + 3);
                pieceTable.Delete(2, 3);
                str = str.Substring(0, 2) + str.Substring(2 + 3);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_2()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\r\n\r");
                str = str.Substring(0, 0) + "\n\r\n\r" + str.Substring(0);
                pieceTable.Insert(2, "\n\r\r\r");
                str = str.Substring(0, 2) + "\n\r\r\r" + str.Substring(2);
                pieceTable.Delete(4, 1);
                str = str.Substring(0, 4) + str.Substring(4 + 1);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_3()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\n\n\r");
                str = str.Substring(0, 0) + "\n\n\n\r" + str.Substring(0);
                pieceTable.Delete(2, 2);
                str = str.Substring(0, 2) + str.Substring(2 + 2);
                pieceTable.Delete(0, 2);
                str = str.Substring(0, 0) + str.Substring(0 + 2);
                pieceTable.Insert(0, "\r\r\r\r");
                str = str.Substring(0, 0) + "\r\r\r\r" + str.Substring(0);
                pieceTable.Insert(2, "\r\n\r\r");
                str = str.Substring(0, 2) + "\r\n\r\r" + str.Substring(2);
                pieceTable.Insert(3, "\r\r\r\n");
                str = str.Substring(0, 3) + "\r\r\r\n" + str.Substring(3);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_4()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\n\n\n");
                str = str.Substring(0, 0) + "\n\n\n\n" + str.Substring(0);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Insert(1, "\r\r\r\r");
                str = str.Substring(0, 1) + "\r\r\r\r" + str.Substring(1);
                pieceTable.Insert(6, "\r\n\n\r");
                str = str.Substring(0, 6) + "\r\n\n\r" + str.Substring(6);
                pieceTable.Delete(5, 3);
                str = str.Substring(0, 5) + str.Substring(5 + 3);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_5()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\n\n\n");
                str = str.Substring(0, 0) + "\n\n\n\n" + str.Substring(0);
                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Insert(0, "\n\r\r\n");
                str = str.Substring(0, 0) + "\n\r\r\n" + str.Substring(0);
                pieceTable.Insert(4, "\n\r\r\n");
                str = str.Substring(0, 4) + "\n\r\r\n" + str.Substring(4);
                pieceTable.Delete(4, 3);
                str = str.Substring(0, 4) + str.Substring(4 + 3);
                pieceTable.Insert(5, "\r\r\n\r");
                str = str.Substring(0, 5) + "\r\r\n\r" + str.Substring(5);
                pieceTable.Insert(12, "\n\n\n\r");
                str = str.Substring(0, 12) + "\n\n\n\r" + str.Substring(12);
                pieceTable.Insert(5, "\r\r\r\n");
                str = str.Substring(0, 5) + "\r\r\r\n" + str.Substring(5);
                pieceTable.Insert(20, "\n\n\r\n");
                str = str.Substring(0, 20) + "\n\n\r\n" + str.Substring(20);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_6()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\r\r\n");
                str = str.Substring(0, 0) + "\n\r\r\n" + str.Substring(0);
                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(3, "\r\n\n\n");
                str = str.Substring(0, 3) + "\r\n\n\n" + str.Substring(3);
                pieceTable.Delete(4, 8);
                str = str.Substring(0, 4) + str.Substring(4 + 8);
                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(0, "\r\n\n\r");
                str = str.Substring(0, 0) + "\r\n\n\r" + str.Substring(0);
                pieceTable.Delete(4, 0);
                str = str.Substring(0, 4) + str.Substring(4 + 0);
                pieceTable.Delete(8, 4);
                str = str.Substring(0, 8) + str.Substring(8 + 4);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_8()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\r\n\n\r");
                str = str.Substring(0, 0) + "\r\n\n\r" + str.Substring(0);
                pieceTable.Delete(1, 0);
                str = str.Substring(0, 1) + str.Substring(1 + 0);
                pieceTable.Insert(3, "\n\n\n\r");
                str = str.Substring(0, 3) + "\n\n\n\r" + str.Substring(3);
                pieceTable.Insert(7, "\n\n\r\n");
                str = str.Substring(0, 7) + "\n\n\r\n" + str.Substring(7);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_7()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\r\r\n\n");
                str = str.Substring(0, 0) + "\r\r\n\n" + str.Substring(0);
                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(7, "\n\r\r\r");
                str = str.Substring(0, 7) + "\n\r\r\r" + str.Substring(7);
                pieceTable.Insert(11, "\n\n\r\n");
                str = str.Substring(0, 11) + "\n\n\r\n" + str.Substring(11);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_10()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "qneW");
                str = str.Substring(0, 0) + "qneW" + str.Substring(0);
                pieceTable.Insert(0, "YhIl");
                str = str.Substring(0, 0) + "YhIl" + str.Substring(0);
                pieceTable.Insert(0, "qdsm");
                str = str.Substring(0, 0) + "qdsm" + str.Substring(0);
                pieceTable.Delete(7, 0);
                str = str.Substring(0, 7) + str.Substring(7 + 0);
                pieceTable.Insert(12, "iiPv");
                str = str.Substring(0, 12) + "iiPv" + str.Substring(12);
                pieceTable.Insert(9, "V\rSA");
                str = str.Substring(0, 9) + "V\rSA" + str.Substring(9);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_9()
            {
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer("");

                pieceTable.Insert(0, "\n\n\n\n");
                str = str.Substring(0, 0) + "\n\n\n\n" + str.Substring(0);
                pieceTable.Insert(3, "\n\r\n\r");
                str = str.Substring(0, 3) + "\n\r\n\r" + str.Substring(3);
                pieceTable.Insert(2, "\n\r\n\n");
                str = str.Substring(0, 2) + "\n\r\n\n" + str.Substring(2);
                pieceTable.Insert(0, "\n\n\r\r");
                str = str.Substring(0, 0) + "\n\n\r\r" + str.Substring(0);
                pieceTable.Insert(3, "\r\r\r\r");
                str = str.Substring(0, 3) + "\r\r\r\r" + str.Substring(3);
                pieceTable.Insert(3, "\n\n\r\r");
                str = str.Substring(0, 3) + "\n\n\r\r" + str.Substring(3);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class CentralizedLineStartsWithCrlf
        {
            [Test]
            public void DeleteCrInCrlf_1()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("a\r\nb");
                pieceTable.Delete(2, 2);
                Assert.AreEqual(2, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void DeleteCrInCrlf_2()
            {
                var pieceTable = CreateTextBuffer("a\r\nb");
                pieceTable.Delete(0, 2);

                Assert.AreEqual(2, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_1()
            {
                var str = "\n\n\r\r";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\n\r\r");
                pieceTable.Insert(1, "\r\n\r\n");
                str = str.Substring(0, 1) + "\r\n\r\n" + str.Substring(1);
                pieceTable.Delete(5, 3);
                str = str.Substring(0, 5) + str.Substring(5 + 3);
                pieceTable.Delete(2, 3);
                str = str.Substring(0, 2) + str.Substring(2 + 3);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_2()
            {
                var str = "\n\r\n\r";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\r\n\r");

                pieceTable.Insert(2, "\n\r\r\r");
                str = str.Substring(0, 2) + "\n\r\r\r" + str.Substring(2);
                pieceTable.Delete(4, 1);
                str = str.Substring(0, 4) + str.Substring(4 + 1);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_3()
            {
                var str = "\n\n\n\r";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\n\n\r");

                pieceTable.Delete(2, 2);
                str = str.Substring(0, 2) + str.Substring(2 + 2);
                pieceTable.Delete(0, 2);
                str = str.Substring(0, 0) + str.Substring(0 + 2);
                pieceTable.Insert(0, "\r\r\r\r");
                str = str.Substring(0, 0) + "\r\r\r\r" + str.Substring(0);
                pieceTable.Insert(2, "\r\n\r\r");
                str = str.Substring(0, 2) + "\r\n\r\r" + str.Substring(2);
                pieceTable.Insert(3, "\r\r\r\n");
                str = str.Substring(0, 3) + "\r\r\r\n" + str.Substring(3);

                var lines = SplitLines(str);
                Assert.AreEqual(lines.Length, pieceTable.LineCount);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_4()
            {
                var str = "\n\n\n\n";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\n\n\n");

                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Insert(1, "\r\r\r\r");
                str = str.Substring(0, 1) + "\r\r\r\r" + str.Substring(1);
                pieceTable.Insert(6, "\r\n\n\r");
                str = str.Substring(0, 6) + "\r\n\n\r" + str.Substring(6);
                pieceTable.Delete(5, 3);
                str = str.Substring(0, 5) + str.Substring(5 + 3);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_5()
            {
                var str = "\n\n\n\n";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\n\n\n");

                pieceTable.Delete(3, 1);
                str = str.Substring(0, 3) + str.Substring(3 + 1);
                pieceTable.Insert(0, "\n\r\r\n");
                str = str.Substring(0, 0) + "\n\r\r\n" + str.Substring(0);
                pieceTable.Insert(4, "\n\r\r\n");
                str = str.Substring(0, 4) + "\n\r\r\n" + str.Substring(4);
                pieceTable.Delete(4, 3);
                str = str.Substring(0, 4) + str.Substring(4 + 3);
                pieceTable.Insert(5, "\r\r\n\r");
                str = str.Substring(0, 5) + "\r\r\n\r" + str.Substring(5);
                pieceTable.Insert(12, "\n\n\n\r");
                str = str.Substring(0, 12) + "\n\n\n\r" + str.Substring(12);
                pieceTable.Insert(5, "\r\r\r\n");
                str = str.Substring(0, 5) + "\r\r\r\n" + str.Substring(5);
                pieceTable.Insert(20, "\n\n\r\n");
                str = str.Substring(0, 20) + "\n\n\r\n" + str.Substring(20);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_6()
            {
                var str = "\n\r\r\n";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\r\r\n");

                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(3, "\r\n\n\n");
                str = str.Substring(0, 3) + "\r\n\n\n" + str.Substring(3);
                pieceTable.Delete(4, 8);
                str = str.Substring(0, 4) + str.Substring(4 + 8);
                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(0, "\r\n\n\r");
                str = str.Substring(0, 0) + "\r\n\n\r" + str.Substring(0);
                pieceTable.Delete(4, 0);
                str = str.Substring(0, 4) + str.Substring(4 + 0);
                pieceTable.Delete(8, 4);
                str = str.Substring(0, 8) + str.Substring(8 + 4);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_7()
            {
                var str = "\r\n\n\r";
                var pieceTable = CreateNonNormalizedTextBuffer("\r\n\n\r");

                pieceTable.Delete(1, 0);
                str = str.Substring(0, 1) + str.Substring(1 + 0);
                pieceTable.Insert(3, "\n\n\n\r");
                str = str.Substring(0, 3) + "\n\n\n\r" + str.Substring(3);
                pieceTable.Insert(7, "\n\n\r\n");
                str = str.Substring(0, 7) + "\n\n\r\n" + str.Substring(7);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_8()
            {
                var str = "\r\r\n\n";
                var pieceTable = CreateNonNormalizedTextBuffer("\r\r\n\n");

                pieceTable.Insert(4, "\r\n\n\r");
                str = str.Substring(0, 4) + "\r\n\n\r" + str.Substring(4);
                pieceTable.Insert(7, "\n\r\r\r");
                str = str.Substring(0, 7) + "\n\r\r\r" + str.Substring(7);
                pieceTable.Insert(11, "\n\n\r\n");
                str = str.Substring(0, 11) + "\n\n\r\n" + str.Substring(11);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_9()
            {
                var str = "qneW";
                var pieceTable = CreateNonNormalizedTextBuffer("qneW");

                pieceTable.Insert(0, "YhIl");
                str = str.Substring(0, 0) + "YhIl" + str.Substring(0);
                pieceTable.Insert(0, "qdsm");
                str = str.Substring(0, 0) + "qdsm" + str.Substring(0);
                pieceTable.Delete(7, 0);
                str = str.Substring(0, 7) + str.Substring(7 + 0);
                pieceTable.Insert(12, "iiPv");
                str = str.Substring(0, 12) + "iiPv" + str.Substring(12);
                pieceTable.Insert(9, "V\rSA");
                str = str.Substring(0, 9) + "V\rSA" + str.Substring(9);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomBug_10()
            {
                var str = "\n\n\n\n";
                var pieceTable = CreateNonNormalizedTextBuffer("\n\n\n\n");

                pieceTable.Insert(3, "\n\r\n\r");
                str = str.Substring(0, 3) + "\n\r\n\r" + str.Substring(3);
                pieceTable.Insert(2, "\n\r\n\n");
                str = str.Substring(0, 2) + "\n\r\n\n" + str.Substring(2);
                pieceTable.Insert(0, "\n\n\r\r");
                str = str.Substring(0, 0) + "\n\n\r\r" + str.Substring(0);
                pieceTable.Insert(3, "\r\r\r\r");
                str = str.Substring(0, 3) + "\r\r\r\r" + str.Substring(3);
                pieceTable.Insert(3, "\n\n\r\r");
                str = str.Substring(0, 3) + "\n\n\r\r" + str.Substring(3);

                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunkBug_1()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("\n\r\r\n\n\n\r\n\r");
                var str = "\n\r\r\n\n\n\r\n\r";
                pieceTable.Delete(0, 2);
                str = str.Substring(0, 0) + str.Substring(0 + 2);
                pieceTable.Insert(1, "\r\r\n\n");
                str = str.Substring(0, 1) + "\r\r\n\n" + str.Substring(1);
                pieceTable.Insert(7, "\r\r\r\r");
                str = str.Substring(0, 7) + "\r\r\r\r" + str.Substring(7);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunkBug_2()
            {
                var pieceTable = CreateNonNormalizedTextBuffer(
                    "\n\r\n\n\n\r\n\r\n\r\r\n\n\n\r\r\n\r\n"
                );
                var str = "\n\r\n\n\n\r\n\r\n\r\r\n\n\n\r\r\n\r\n";
                pieceTable.Insert(16, "\r\n\r\r");
                str = str.Substring(0, 16) + "\r\n\r\r" + str.Substring(16);
                pieceTable.Insert(13, "\n\n\r\r");
                str = str.Substring(0, 13) + "\n\n\r\r" + str.Substring(13);
                pieceTable.Insert(19, "\n\n\r\n");
                str = str.Substring(0, 19) + "\n\n\r\n" + str.Substring(19);
                pieceTable.Delete(5, 0);
                str = str.Substring(0, 5) + str.Substring(5 + 0);
                pieceTable.Delete(11, 2);
                str = str.Substring(0, 11) + str.Substring(11 + 2);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunkBug_3()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("\r\n\n\n\n\n\n\r\n");
                var str = "\r\n\n\n\n\n\n\r\n";
                pieceTable.Insert(4, "\n\n\r\n\r\r\n\n\r");
                str = str.Substring(0, 4) + "\n\n\r\n\r\r\n\n\r" + str.Substring(4);
                pieceTable.Delete(4, 4);
                str = str.Substring(0, 4) + str.Substring(4 + 4);
                pieceTable.Insert(11, "\r\n\r\n\n\r\r\n\n");
                str = str.Substring(0, 11) + "\r\n\r\n\n\r\r\n\n" + str.Substring(11);
                pieceTable.Delete(1, 2);
                str = str.Substring(0, 1) + str.Substring(1 + 2);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunkBug_4()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("\n\r\n\r");
                var str = "\n\r\n\r";
                pieceTable.Insert(4, "\n\n\r\n");
                str = str.Substring(0, 4) + "\n\n\r\n" + str.Substring(4);
                pieceTable.Insert(3, "\r\n\n\n");
                str = str.Substring(0, 3) + "\r\n\n\n" + str.Substring(3);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class RandomIsUnsupervised
        {
            [Test]
            public void SplittingLargeChangeBufferFun()
            {
                var pieceTable = CreateNonNormalizedTextBuffer("");
                var str = "";

                pieceTable.Insert(0, "WUZ\nXVZY\n");
                str = str.Substring(0, 0) + "WUZ\nXVZY\n" + str.Substring(0);
                pieceTable.Insert(8, "\r\r\nZXUWVW");
                str = str.Substring(0, 8) + "\r\r\nZXUWVW" + str.Substring(8);
                pieceTable.Delete(10, 7);
                str = str.Substring(0, 10) + str.Substring(10 + 7);
                pieceTable.Delete(10, 1);
                str = str.Substring(0, 10) + str.Substring(10 + 1);
                pieceTable.Insert(4, "VX\r\r\nWZVZ");
                str = str.Substring(0, 4) + "VX\r\r\nWZVZ" + str.Substring(4);
                pieceTable.Delete(11, 3);
                str = str.Substring(0, 11) + str.Substring(11 + 3);
                pieceTable.Delete(12, 4);
                str = str.Substring(0, 12) + str.Substring(12 + 4);
                pieceTable.Delete(8, 0);
                str = str.Substring(0, 8) + str.Substring(8 + 0);
                pieceTable.Delete(10, 2);
                str = str.Substring(0, 10) + str.Substring(10 + 2);
                pieceTable.Insert(0, "VZXXZYZX\r");
                str = str.Substring(0, 0) + "VZXXZYZX\r" + str.Substring(0);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomInsertDeleteFun()
            {
                // this.timeout(500000);
                var str = "";
                var pieceTable = CreateNonNormalizedTextBuffer(str);

                // var output = "";
                for (var i = 0; i < 1000; i++) {
                    if (random.NextDouble() < 0.6) {
                        // insert
                        var text = RandomStr(100);
                        var pos = RandomInt(str.Length + 1);
                        pieceTable.Insert(pos, text);
                        str = str.Substring(0, pos) + text + str.Substring(pos);
                        // output += `pieceTable.Insert(${pos}, "${text.replace(/\n/g, "\\n").replace(/\r/g, "\\r")}");\n`;
                        // output += `str = str.Substring(0, ${pos}) + "${text.replace(/\n/g, "\\n").replace(/\r/g, "\\r")}" + str.Substring(${pos});\n`;
                    } else {
                        // delete
                        var pos = RandomInt(str.Length);
                        var length = Math.Min(
                            str.Length - pos,
                            random.Next(10)
                        );
                        pieceTable.Delete(pos, length);
                        str = str.Substring(0, pos) + str.Substring(pos + length);
                        // output += `pieceTable.Delete(${pos}, ${length});\n`;
                        // output += `str = str.Substring(0, ${pos}) + str.substring(${pos} + ${length});\n`

                    }
                }
                // console.log(output);

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunksFun()
            {
                // this.timeout(500000);
                var chunks = new List<string>();
                for (var i = 0; i < 5; i++) {
                    chunks.Add(RandomStr(1000));
                }

                var pieceTable = CreateTextBuffer(chunks.ToArray(), false);
                var str = String.Join("", chunks);

                for (var i = 0; i < 1000; i++) {
                    if (random.NextDouble() < 0.6) {
                        // insert
                        var text = RandomStr(100);
                        var pos = RandomInt(str.Length + 1);
                        pieceTable.Insert(pos, text);
                        str = str.Substring(0, pos) + text + str.Substring(pos);
                    } else {
                        // delete
                        var pos = RandomInt(str.Length);
                        var length = Math.Min(
                            str.Length - pos,
                            random.Next(10)
                        );
                        pieceTable.Delete(pos, length);
                        str = str.Substring(0, pos) + str.Substring(pos + length);
                    }
                }

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void RandomChunks_2Fun()
            {
                // this.timeout(500000);
                var chunks = new List<string>();
                chunks.Add(RandomStr(1000));

                var pieceTable = CreateTextBuffer(chunks.ToArray(), false);
                var str = String.Join("", chunks);

                for (var i = 0; i < 50; i++) {
                    if (random.NextDouble() < 0.6) {
                        // insert
                        var text = RandomStr(30);
                        var pos = RandomInt(str.Length + 1);
                        pieceTable.Insert(pos, text);
                        str = str.Substring(0, pos) + text + str.Substring(pos);
                    } else {
                        // delete
                        var pos = RandomInt(str.Length);
                        var length = Math.Min(
                            str.Length - pos,
                            random.Next(10)
                        );
                        pieceTable.Delete(pos, length);
                        str = str.Substring(0, pos) + str.Substring(pos + length);
                    }
                    TestLinesContent(str, pieceTable);
                }

                Assert.AreEqual(str, pieceTable.GetLinesRawContent());
                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

        public class BufferApi
        {
            [Test]
            public void Equal()
            {
                var a = CreateTextBuffer("abc");
                var b = CreateTextBuffer("ab", "c");
                var c = CreateTextBuffer("abd");
                var d = CreateTextBuffer("abcd");

                Assert.True(a.Equals(b));
                Assert.True(!a.Equals(c));
                Assert.True(!a.Equals(d));
            }

            [Test]
            public void Equal_2EmptyBuffer()
            {
                var a = CreateTextBuffer("");
                var b = CreateTextBuffer("");

                Assert.True(a.Equals(b));
            }

            [Test]
            public void Equal_3EmptyBuffer()
            {
                var a = CreateTextBuffer("a");
                var b = CreateTextBuffer("");

                Assert.True(!a.Equals(b));
            }

            [Test]
            public void GetLineCharCodeIssue_45735()
            {
                var pieceTable = CreateTextBuffer("LINE1\nline2");
                Assert.AreEqual('L', pieceTable.GetLineCharCode(1, 0));
                Assert.AreEqual('I', pieceTable.GetLineCharCode(1, 1));
                Assert.AreEqual('N', pieceTable.GetLineCharCode(1, 2));
                Assert.AreEqual('E', pieceTable.GetLineCharCode(1, 3));
                Assert.AreEqual('1', pieceTable.GetLineCharCode(1, 4));
                Assert.AreEqual('\n', pieceTable.GetLineCharCode(1, 5));
                Assert.AreEqual('l', pieceTable.GetLineCharCode(2, 0));
                Assert.AreEqual('i', pieceTable.GetLineCharCode(2, 1));
                Assert.AreEqual('n', pieceTable.GetLineCharCode(2, 2));
                Assert.AreEqual('e', pieceTable.GetLineCharCode(2, 3));
                Assert.AreEqual('2', pieceTable.GetLineCharCode(2, 4));
            }

            [Test]
            public void GetLineCharCodeIssue_47733()
            {
                var pieceTable = CreateTextBuffer("", "LINE1\n", "line2");
                Assert.AreEqual('L', pieceTable.GetLineCharCode(1, 0));
                Assert.AreEqual('I', pieceTable.GetLineCharCode(1, 1));
                Assert.AreEqual('N', pieceTable.GetLineCharCode(1, 2));
                Assert.AreEqual('E', pieceTable.GetLineCharCode(1, 3));
                Assert.AreEqual('1', pieceTable.GetLineCharCode(1, 4));
                Assert.AreEqual('\n', pieceTable.GetLineCharCode(1, 5));
                Assert.AreEqual('l', pieceTable.GetLineCharCode(2, 0));
                Assert.AreEqual('i', pieceTable.GetLineCharCode(2, 1));
                Assert.AreEqual('n', pieceTable.GetLineCharCode(2, 2));
                Assert.AreEqual('e', pieceTable.GetLineCharCode(2, 3));
                Assert.AreEqual('2', pieceTable.GetLineCharCode(2, 4));
            }
        }

        public class SearchOffsetCache
        {
            [Test]
            public void RenderWhiteSpaceException()
            {
                var pieceTable = CreateTextBuffer("class Name{\n\t\n\t\t\tget() {\n\n\t\t\t}\n\t\t}");
                var str = "class Name{\n\t\n\t\t\tget() {\n\n\t\t\t}\n\t\t}";

                pieceTable.Insert(12, "s");
                str = str.Substring(0, 12) + "s" + str.Substring(12);

                pieceTable.Insert(13, "e");
                str = str.Substring(0, 13) + "e" + str.Substring(13);

                pieceTable.Insert(14, "t");
                str = str.Substring(0, 14) + "t" + str.Substring(14);

                pieceTable.Insert(15, "()");
                str = str.Substring(0, 15) + "()" + str.Substring(15);

                pieceTable.Delete(16, 1);
                str = str.Substring(0, 16) + str.Substring(16 + 1);

                pieceTable.Insert(17, "()");
                str = str.Substring(0, 17) + "()" + str.Substring(17);

                pieceTable.Delete(18, 1);
                str = str.Substring(0, 18) + str.Substring(18 + 1);

                pieceTable.Insert(18, "}");
                str = str.Substring(0, 18) + "}" + str.Substring(18);

                pieceTable.Insert(12, "\n");
                str = str.Substring(0, 12) + "\n" + str.Substring(12);

                pieceTable.Delete(12, 1);
                str = str.Substring(0, 12) + str.Substring(12 + 1);

                pieceTable.Delete(18, 1);
                str = str.Substring(0, 18) + str.Substring(18 + 1);

                pieceTable.Insert(18, "}");
                str = str.Substring(0, 18) + "}" + str.Substring(18);

                pieceTable.Delete(17, 2);
                str = str.Substring(0, 17) + str.Substring(17 + 2);

                pieceTable.Delete(16, 1);
                str = str.Substring(0, 16) + str.Substring(16 + 1);

                pieceTable.Insert(16, ")");
                str = str.Substring(0, 16) + ")" + str.Substring(16);

                pieceTable.Delete(15, 2);
                str = str.Substring(0, 15) + str.Substring(15 + 2);

                var content = pieceTable.GetLinesRawContent();
                Assert.AreEqual(str, content);
            }

            [Test]
            public void LineBreaksReplacementIsNotNecessaryWhenEolIsNormalized()
            {
                var pieceTable = CreateTextBuffer("abc");
                var str = "abc";

                pieceTable.Insert(3, "def\nabc");
                str = str + "def\nabc";

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void LineBreaksReplacementIsNotNecessaryWhenEolIsNormalized_2()
            {
                var pieceTable = CreateTextBuffer("abc\n");
                var str = "abc\n";

                pieceTable.Insert(4, "def\nabc");
                str = str + "def\nabc";

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void LineBreaksReplacementIsNotNecessaryWhenEolIsNormalized_3()
            {
                var pieceTable = CreateTextBuffer("abc\n");
                var str = "abc\n";

                pieceTable.Insert(2, "def\nabc");
                str = str.Substring(0, 2) + "def\nabc" + str.Substring(2);

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }

            [Test]
            public void LineBreaksReplacementIsNotNecessaryWhenEolIsNormalized_4()
            {
                var pieceTable = CreateTextBuffer("abc\n");
                var str = "abc\n";

                pieceTable.Insert(3, "def\nabc");
                str = str.Substring(0, 3) + "def\nabc" + str.Substring(3);

                TestLineStarts(str, pieceTable);
                TestLinesContent(str, pieceTable);
                AssertTreeInvariants(pieceTable);
            }
        }

/*
        string getValueInSnapshot(ITextSnapshot snapshot) {
            var ret = "";
            var tmp = snapshot.read();

            while (tmp != null) {
                ret += tmp;
                tmp = snapshot.read();
            }

            return ret;
        }

        public class Snapshot
        {
            [Test]
            public void Bug_45564PieceTreePiecesShouldBeImmutable()
            {
                var model = createTextModel("\n");
                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 1),
                        text: "!"
                    }
                ]);
                var snapshot = model.createSnapshot();
                var snapshot1 = model.createSnapshot();
                Assert.AreEqual(getValueInSnapshot(snapshot), model.getLinesContent().join("\n"));

                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 2),
                        text: ""
                    }
                ]);
                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 1),
                        text: "!"
                    }
                ]);

                Assert.AreEqual(getValueInSnapshot(snapshot1), model.getLinesContent().join("\n"));
            }

            [Test]
            public void ImmutableSnapshot_1()
            {
                var model = createTextModel("abc\ndef");
                var snapshot = model.createSnapshot();
                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 4),
                        text: ""
                    }
                ]);

                model.applyEdits([
                    {
                        range: new Range(1, 1, 2, 1),
                        text: "abc\ndef"
                    }
                ]);

                Assert.AreEqual(getValueInSnapshot(snapshot), model.getLinesContent().join("\n"));
            }

            [Test]
            public void ImmutableSnapshot_2()
            {
                var model = createTextModel("abc\ndef");
                var snapshot = model.createSnapshot();
                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 1),
                        text: "!"
                    }
                ]);

                model.applyEdits([
                    {
                        range: new Range(2, 1, 2, 2),
                        text: ""
                    }
                ]);

                Assert.AreEqual(getValueInSnapshot(snapshot), model.getLinesContent().join("\n"));
            }

            [Test]
            public void ImmutableSnapshot_3()
            {
                var model = createTextModel("abc\ndef");
                model.applyEdits([
                    {
                        range: new Range(2, 4, 2, 4),
                        text: "!"
                    }
                ]);
                var snapshot = model.createSnapshot();
                model.applyEdits([
                    {
                        range: new Range(2, 5, 2, 5),
                        text: "!"
                    }
                ]);

                assert.notStrictEqual(model.getLinesContent().join("\n"), getValueInSnapshot(snapshot));
            }
        }

        public class ChunkBasedSearch
        {
            [Test]
            public void _45892ForSomeCasesTheBufferIsEmptyButWeStillTryToSearch()
            {
                var pieceTree = createTextBuffer("");
                pieceTree.delete(0, 1);
                var ret = pieceTree.findMatchesLineByLine(new Range(1, 1, 1, 1), new SearchData(/abc/, new WordCharacterClassifier(",./"), "abc"), true, 1000);
                Assert.AreEqual(0, ret.Length);
            }

            [Test]
            public void _45770FindInNodeShouldNotCrossNodeBoundary()
            {
                var pieceTree = createTextBuffer([
                    [
                        "balabalababalabalababalabalaba",
                        "balabalababalabalababalabalaba",
                        "",
                        "* [ ] task1",
                        "* [x] task2 balabalaba",
                        "* [ ] task 3"
                    ].join("\n")
                ]);
                pieceTree.delete(0, 62);
                pieceTree.delete(16, 1);

                pieceTree.insert(16, " ");
                var ret = pieceTree.findMatchesLineByLine(new Range(1, 1, 4, 13), new SearchData(/\[/gi, new WordCharacterClassifier(",./"), "["), true, 1000);
                Assert.AreEqual(3, ret.Length);

                Assert.AreEqual(ret[0].range, new Range(2, 3, 2, 4));
                Assert.AreEqual(ret[1].range, new Range(3, 3, 3, 4));
                Assert.AreEqual(ret[2].range, new Range(4, 3, 4, 4));
            }

            [Test]
            public void SearchSearchingFromTheMiddle()
            {
                var pieceTree = createTextBuffer([
                    [
                        "def",
                        "dbcabc"
                    ].join("\n")
                ]);
                pieceTree.delete(4, 1);
                var ret = pieceTree.findMatchesLineByLine(new Range(2, 3, 2, 6), new SearchData(/a/gi, null, "a"), true, 1000);
                Assert.AreEqual(1, ret.Length);
                Assert.AreEqual(ret[0].range, new Range(2, 3, 2, 4));

                pieceTree.delete(4, 1);
                ret = pieceTree.findMatchesLineByLine(new Range(2, 2, 2, 5), new SearchData(/a/gi, null, "a"), true, 1000);
                Assert.AreEqual(1, ret.Length);
                Assert.AreEqual(ret[0].range, new Range(2, 2, 2, 3));
            }
        }
*/
    }

}
