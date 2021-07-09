/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Collections.Generic;

namespace PieceTree
{

    public class TreeNode {
        public TreeNode parent { get; internal set; }
        public TreeNode left { get; internal set; }
        public TreeNode right { get; internal set; }
        public NodeColor color { get; internal set; }

        // Piece
        internal Piece piece;
        public int size_left { get; internal set; } // size of the left subtree (not inorder)
        public int lf_left { get; internal set; } // line feeds cnt in the left subtree (not in order)
        public int length => piece.length;
        public int lineFeedCnt => piece.lineFeedCnt;

        internal TreeNode(Piece piece, NodeColor color) {
            this.piece = piece;
            this.color = color;
            this.size_left = 0;
            this.lf_left = 0;
            this.parent = this;
            this.left = this;
            this.right = this;
        }

        public TreeNode Next() {
            if (this.right != TreeNode.SENTINEL) {
                return this.right.Leftest();
            }

            var node = this;

            while (node.parent != TreeNode.SENTINEL) {
                if (node.parent.left == node) {
                    break;
                }

                node = node.parent;
            }

            if (node.parent == TreeNode.SENTINEL) {
                return SENTINEL;
            } else {
                return node.parent;
            }
        }

        public TreeNode Prev() {
            if (this.left != TreeNode.SENTINEL) {
                return this.left.Rightest();
            }

            var node = this;

            while (node.parent != TreeNode.SENTINEL) {
                if (node.parent.right == node) {
                    break;
                }

                node = node.parent;
            }

            if (node.parent == TreeNode.SENTINEL) {
                return SENTINEL;
            } else {
                return node.parent;
            }
        }

        public void Detach() {
            this.parent = null;
            this.left = null;
            this.right = null;
        }

        public IEnumerator<TreeNode> GetEnumerator()
        {
            if (this.left != TreeNode.SENTINEL)
                foreach (var v in this.left)
                    yield return v;

            yield return this;

            if (this.right != TreeNode.SENTINEL)
                foreach (var v in this.right)
                    yield return v;
        }

        public static readonly TreeNode SENTINEL = CreateSentinel();

        private static TreeNode CreateSentinel()
        {
            var sentinel = new TreeNode(null, NodeColor.Black);
            sentinel.parent = sentinel;
            sentinel.left = sentinel;
            sentinel.right = sentinel;
            sentinel.color = NodeColor.Black;
            return sentinel;
        }

    }

    public enum NodeColor {
        Black = 0,
        Red = 1,
    }

    public static class TreeNodeExtensions
    {

        public static TreeNode Leftest(this TreeNode node)
        {
            while (node.left != TreeNode.SENTINEL)
            {
                node = node.left;
            }
            return node;
        }

        public static TreeNode Rightest(this TreeNode node)
        {
            while (node.right != TreeNode.SENTINEL)
            {
                node = node.right;
            }
            return node;
        }

        public static int CalculateSize(this TreeNode node)
        {
            if (node == TreeNode.SENTINEL)
            {
                return 0;
            }

            return node.size_left + node.piece.length + CalculateSize(node.right);
        }

        public static int CalculateLF(this TreeNode node)
        {
            if (node == TreeNode.SENTINEL)
            {
                return 0;
            }

            return node.lf_left + node.piece.lineFeedCnt + CalculateLF(node.right);
        }

        public static void ResetSentinel()
        {
            TreeNode.SENTINEL.parent = TreeNode.SENTINEL;
        }

        public static void LeftRotate(this PieceTree tree, TreeNode x)
        {
            var y = x.right;

            // fix size_left
            y.size_left += x.size_left + (x.piece != null ? x.piece.length : 0);
            y.lf_left += x.lf_left + (x.piece != null ? x.piece.lineFeedCnt : 0);
            x.right = y.left;

            if (y.left != TreeNode.SENTINEL)
            {
                y.left.parent = x;
            }
            y.parent = x.parent;
            if (x.parent == TreeNode.SENTINEL)
            {
                tree.root = y;
            }
            else if (x.parent.left == x)
            {
                x.parent.left = y;
            }
            else
            {
                x.parent.right = y;
            }
            y.left = x;
            x.parent = y;
        }

        public static void RightRotate(this PieceTree tree, TreeNode y)
        {
            var x = y.left;
            y.left = x.right;
            if (x.right != TreeNode.SENTINEL)
            {
                x.right.parent = y;
            }
            x.parent = y.parent;

            // fix size_left
            y.size_left -= x.size_left + (x.piece != null ? x.piece.length : 0);
            y.lf_left -= x.lf_left + (x.piece != null ? x.piece.lineFeedCnt : 0);

            if (y.parent == TreeNode.SENTINEL)
            {
                tree.root = x;
            }
            else if (y == y.parent.right)
            {
                y.parent.right = x;
            }
            else
            {
                y.parent.left = x;
            }

            x.right = y;
            y.parent = x;
        }

        public static void RbDelete(this PieceTree tree, TreeNode z)
        {
            TreeNode x;
            TreeNode y;

            if (z.left == TreeNode.SENTINEL)
            {
                y = z;
                x = y.right;
            }
            else if (z.right == TreeNode.SENTINEL)
            {
                y = z;
                x = y.left;
            }
            else
            {
                y = Leftest(z.right);
                x = y.right;
            }

            if (y == tree.root)
            {
                tree.root = x;

                // if x is null, we are removing the only node
                x.color = NodeColor.Black;
                z.Detach();
                ResetSentinel();
                tree.root.parent = TreeNode.SENTINEL;

                return;
            }

            var yWasRed = (y.color == NodeColor.Red);

            if (y == y.parent.left)
            {
                y.parent.left = x;
            }
            else
            {
                y.parent.right = x;
            }

            if (y == z)
            {
                x.parent = y.parent;
                tree.RecomputeTreeMetadata(x);
            }
            else
            {
                if (y.parent == z)
                {
                    x.parent = y;
                }
                else
                {
                    x.parent = y.parent;
                }

                // as we make changes to x's hierarchy, update size_left of subtree first
                tree.RecomputeTreeMetadata(x);

                y.left = z.left;
                y.right = z.right;
                y.parent = z.parent;
                y.color = z.color;

                if (z == tree.root)
                {
                    tree.root = y;
                }
                else
                {
                    if (z == z.parent.left)
                    {
                        z.parent.left = y;
                    }
                    else
                    {
                        z.parent.right = y;
                    }
                }

                if (y.left != TreeNode.SENTINEL)
                {
                    y.left.parent = y;
                }
                if (y.right != TreeNode.SENTINEL)
                {
                    y.right.parent = y;
                }
                // update metadata
                // we replace z with y, so in this sub tree, the length change is z.item.length
                y.size_left = z.size_left;
                y.lf_left = z.lf_left;
                tree.RecomputeTreeMetadata(y);
            }

            z.Detach();

            if (x.parent.left == x)
            {
                var newSizeLeft = CalculateSize(x);
                var newLFLeft = CalculateLF(x);
                if (newSizeLeft != x.parent.size_left || newLFLeft != x.parent.lf_left)
                {
                    var delta = newSizeLeft - x.parent.size_left;
                    var lf_delta = newLFLeft - x.parent.lf_left;
                    x.parent.size_left = newSizeLeft;
                    x.parent.lf_left = newLFLeft;
                    tree.UpdateTreeMetadata(x.parent, delta, lf_delta);
                }
            }

            tree.RecomputeTreeMetadata(x.parent);

            if (yWasRed)
            {
                ResetSentinel();
                return;
            }

            // RB-DELETE-FIXUP
            TreeNode w;
            while (x != tree.root && x.color == NodeColor.Black)
            {
                if (x == x.parent.left)
                {
                    w = x.parent.right;

                    if (w.color == NodeColor.Red)
                    {
                        w.color = NodeColor.Black;
                        x.parent.color = NodeColor.Red;
                        tree.LeftRotate(x.parent);
                        w = x.parent.right;
                    }

                    if (w.left.color == NodeColor.Black && w.right.color == NodeColor.Black)
                    {
                        w.color = NodeColor.Red;
                        x = x.parent;
                    }
                    else
                    {
                        if (w.right.color == NodeColor.Black)
                        {
                            w.left.color = NodeColor.Black;
                            w.color = NodeColor.Red;
                            tree.RightRotate(w);
                            w = x.parent.right;
                        }

                        w.color = x.parent.color;
                        x.parent.color = NodeColor.Black;
                        w.right.color = NodeColor.Black;
                        tree.LeftRotate(x.parent);
                        x = tree.root;
                    }
                }
                else
                {
                    w = x.parent.left;

                    if (w.color == NodeColor.Red)
                    {
                        w.color = NodeColor.Black;
                        x.parent.color = NodeColor.Red;
                        tree.RightRotate(x.parent);
                        w = x.parent.left;
                    }

                    if (w.left.color == NodeColor.Black && w.right.color == NodeColor.Black)
                    {
                        w.color = NodeColor.Red;
                        x = x.parent;

                    }
                    else
                    {
                        if (w.left.color == NodeColor.Black)
                        {
                            w.right.color = NodeColor.Black;
                            w.color = NodeColor.Red;
                            tree.LeftRotate(w);
                            w = x.parent.left;
                        }

                        w.color = x.parent.color;
                        x.parent.color = NodeColor.Black;
                        w.left.color = NodeColor.Black;
                        tree.RightRotate(x.parent);
                        x = tree.root;
                    }
                }
            }
            x.color = NodeColor.Black;
            ResetSentinel();
        }

        public static void FixInsert(this PieceTree tree, TreeNode x)
        {
            tree.RecomputeTreeMetadata(x);

            while (x != tree.root && x.parent.color == NodeColor.Red)
            {
                if (x.parent == x.parent.parent.left)
                {
                    var y = x.parent.parent.right;

                    if (y.color == NodeColor.Red)
                    {
                        x.parent.color = NodeColor.Black;
                        y.color = NodeColor.Black;
                        x.parent.parent.color = NodeColor.Red;
                        x = x.parent.parent;
                    }
                    else
                    {
                        if (x == x.parent.right)
                        {
                            x = x.parent;
                            tree.LeftRotate(x);
                        }

                        x.parent.color = NodeColor.Black;
                        x.parent.parent.color = NodeColor.Red;
                        tree.RightRotate(x.parent.parent);
                    }
                }
                else
                {
                    var y = x.parent.parent.left;

                    if (y.color == NodeColor.Red)
                    {
                        x.parent.color = NodeColor.Black;
                        y.color = NodeColor.Black;
                        x.parent.parent.color = NodeColor.Red;
                        x = x.parent.parent;
                    }
                    else
                    {
                        if (x == x.parent.left)
                        {
                            x = x.parent;
                            tree.RightRotate(x);
                        }
                        x.parent.color = NodeColor.Black;
                        x.parent.parent.color = NodeColor.Red;
                        tree.LeftRotate(x.parent.parent);
                    }
                }
            }

            tree.root.color = NodeColor.Black;
        }

        public static void UpdateTreeMetadata(this PieceTree tree, TreeNode x, int delta, int lineFeedCntDelta)
        {
            // node length change or line feed count change
            while (x != tree.root && x != TreeNode.SENTINEL)
            {
                if (x.parent.left == x)
                {
                    x.parent.size_left += delta;
                    x.parent.lf_left += lineFeedCntDelta;
                }

                x = x.parent;
            }
        }

        internal static void RecomputeTreeMetadata(this PieceTree tree, TreeNode x)
        {
            var delta = 0;
            var lf_delta = 0;
            if (x == tree.root)
            {
                return;
            }

            if (delta == 0)
            {
                // go upwards till the node whose left subtree is changed.
                while (x != tree.root && x == x.parent.right)
                {
                    x = x.parent;
                }

                if (x == tree.root)
                {
                    // well, it means we add a node to the end (inorder)
                    return;
                }

                // x is the node whose right subtree is changed.
                x = x.parent;

                delta = CalculateSize(x.left) - x.size_left;
                lf_delta = CalculateLF(x.left) - x.lf_left;
                x.size_left += delta;
                x.lf_left += lf_delta;
            }

            // go upwards till root. O(logN)
            while (x != tree.root && (delta != 0 || lf_delta != 0))
            {
                if (x.parent.left == x)
                {
                    x.parent.size_left += delta;
                    x.parent.lf_left += lf_delta;
                }

                x = x.parent;
            }
        }

    }

}
