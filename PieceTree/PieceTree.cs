/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PieceTree
{

    public struct TextPosition
    {
        /**
        * Line number in current buffer
        */
        public readonly int line;
        /**
        * Column number in current buffer
        */
        public readonly int column;

        public TextPosition(int line, int column)
        {
            this.line = line;
            this.column = column;
        }
    }

    public class LineStarts
    {
        public readonly int[] lineStarts;
        public readonly int cr;
        public readonly int lf;
        public readonly int crlf;
        public readonly bool isBasicASCII;
        public readonly string EOL;
        public bool isEOLNormalized => cr == 0 || cr == crlf;

        private LineStarts(int[] lineStarts, int cr, int lf, int crlf, bool isBasicASCII, string defaultEOL = "\n")
        {
            this.lineStarts = lineStarts;
            this.cr = cr;
            this.lf = lf;
            this.crlf = crlf;
            this.isBasicASCII = isBasicASCII;
            this.EOL = GetEOL(defaultEOL);
        }

        internal static int[] CreateLineStartsFast(string str)
        {
            var r = new List<int>();
            r.Add(0);

            for (int i = 0, len = str.Length; i < len; i++)
            {
                var chr = str[i];

                if (chr == '\r')
                {
                    if (i + 1 < len && str[i + 1] == '\n')
                    {
                        // \r\n... case
                        r.Add(i + 2);
                        i++; // skip \n
                    }
                    else
                    {
                        // \r... case
                        r.Add(i + 1);
                    }
                }
                else if (chr == '\n')
                {
                    r.Add(i + 1);
                }
            }
            return r.ToArray();
        }

        public static LineStarts Create(string str, List<int> buffer = null, string defaultEOL = "\n")
        {
            if (buffer == null)
                buffer = new List<int>();
            else
                buffer.Clear();
            buffer.Add(0);
            int cr = 0, lf = 0, crlf = 0;
            var isBasicASCII = true;
            for (int i = 0, len = str.Length; i < len; i++)
            {
                var chr = str[i];

                if (chr == '\r')
                {
                    if (i + 1 < len && str[i + 1] == '\n')
                    {
                        // \r\n... case
                        crlf++;
                        buffer.Add(i + 2);
                        i++; // skip \n
                    }
                    else
                    {
                        cr++;
                        // \r... case
                        buffer.Add(i + 1);
                    }
                }
                else if (chr == '\n')
                {
                    lf++;
                    buffer.Add(i + 1);
                }
                else
                {
                    if (isBasicASCII)
                    {
                        if (chr != '\t' && (chr < 32 || chr > 126))
                        {
                            isBasicASCII = false;
                        }
                    }
                }
            }
            var result = new LineStarts(buffer.ToArray(), cr, lf, crlf, isBasicASCII, defaultEOL);
            buffer.Clear();

            return result;
        }

        private string GetEOL(string defaultEOL = "\n")
        {
            var totalEOLCount = this.cr + this.lf + this.crlf;
            var totalCRCount = this.cr + this.crlf;
            if (totalEOLCount == 0)
            {
                // This is an empty file or a file with precisely one line
                return defaultEOL;
            }
            if (totalCRCount > totalEOLCount / 2)
            {
                // More than half of the file contains \r\n ending lines
                return "\r\n";
            }
            // At least one line more ends in \n
            return "\n";
        }

    }

    public class PieceTree {
        const int AverageBufferSize = 65535;

        public TreeNode root { get; internal set; }
        internal List<StringBuffer> _buffers; // 0 is change buffer, others are readonly original buffer.
        protected int _lineCnt;
        protected int _length;
        protected string _EOL;
        protected int _EOLLength;
        protected bool _EOLNormalized;
        private TextPosition _lastChangeBufferPos;
        private PieceTreeSearchCache _searchCache;
        private int _lastVisitedLineNumber;
        private string _lastVisitedLine;

        public PieceTree(string str)
        {
            var lineStarts = LineStarts.Create(str);
            var buffer = new StringBuffer(str, lineStarts.lineStarts);
            this.Create(new List<StringBuffer>() { buffer }, lineStarts.EOL, lineStarts.isEOLNormalized);
        }

        public PieceTree(IEnumerable<string> strings, string eol, bool eolNormalized)
        {
            var chunks = new List<StringBuffer>();
            foreach (var str in strings)
            {
                var lineStarts = LineStarts.CreateLineStartsFast(str);
                chunks.Add(new StringBuffer(str, lineStarts));
            }
            this.Create(chunks, eol, eolNormalized);
        }

        private void Create(List<StringBuffer> chunks, string eol, bool eolNormalized) {
            this._buffers = new List<StringBuffer>() {
                new StringBuffer("", new int[] { 0 })
            };
            this._lastChangeBufferPos = new TextPosition(0, 0);
            this.root = TreeNode.SENTINEL;
            this._lineCnt = 1;
            this._length = 0;
            this._EOL = eol;
            this._EOLLength = eol.Length;
            this._EOLNormalized = eolNormalized;

            TreeNode lastNode = null;
            var bufferIndex = 1;
            foreach (var chunk in chunks) {
                if (chunk.buffer.Length > 0) {
                    if (chunk.lineStarts.Length == 0) {
                        chunk.lineStarts = LineStarts.CreateLineStartsFast(chunk.buffer);
                    }

                    var piece = new Piece(
                        bufferIndex++,
                        new TextPosition(0, 0),
                        new TextPosition(chunk.lineStarts.Length - 1, chunk.buffer.Length - chunk.lineStarts[chunk.lineStarts.Length - 1]),
                        chunk.lineStarts.Length - 1,
                        chunk.buffer.Length
                    );
                    this._buffers.Add(chunk);
                    lastNode = this.RbInsertRight(lastNode, piece);
                }
            }

            this._searchCache = new PieceTreeSearchCache(1);
            this._lastVisitedLineNumber = 0;
            this._lastVisitedLine = "";
            this.ComputeBufferMetadata();
        }

        private void NormalizeEOL(string eol) {
            var averageBufferSize = AverageBufferSize;
            var min = averageBufferSize - averageBufferSize / 3;
            var max = min * 2;

            var tempChunk = "";
            var tempChunkLen = 0;
            var chunks = new List<StringBuffer>();

            this.Iterate(this.root, node => {
                var str = this.GetNodeContent(node);
                var len = str.Length;
                if (tempChunkLen <= min || tempChunkLen + len < max) {
                    tempChunk += str;
                    tempChunkLen += len;
                    return true;
                }

                // flush anyways
                var text = Regex.Replace(tempChunk, @"\r\n|\r|\n", eol);
                chunks.Add(new StringBuffer(text, LineStarts.CreateLineStartsFast(text)));
                tempChunk = str;
                tempChunkLen = len;
                return true;
            });

            if (tempChunkLen > 0) {
                var text = Regex.Replace(tempChunk, @"\r\n|\r|\n", eol);
                chunks.Add(new StringBuffer(text, LineStarts.CreateLineStartsFast(text)));
            }

            this.Create(chunks, eol, true);
        }


        // #region Buffer API

        public string EOL
        {
            get => this._EOL;
            set
            {
                this._EOL = value;
                this._EOLLength = this._EOL.Length;
                this.NormalizeEOL(value);
            }
        }

        public override bool Equals(object o) {
            if (!(o is PieceTree))
                return false;

            var other = o as PieceTree;
            if (this.Length != other.Length) {
                return false;
            }
            if (this.LineCount != other.LineCount) {
                return false;
            }

            var offset = 0;
            var ret = this.Iterate(this.root, node => {
                if (node == TreeNode.SENTINEL) {
                    return true;
                }
                var str = this.GetNodeContent(node);
                var len = str.Length;
                var startPosition = other.NodeAt(offset);
                var endPosition = other.NodeAt(offset + len);
                var val = other.GetValueInRange(startPosition.Value, endPosition.Value);

                return str == val;
            });

            return ret;
        }

        public int GetOffsetAt(int lineNumber, int column) {
            var leftLen = 0; // inorder

            var x = this.root;

            while (x != TreeNode.SENTINEL) {
                if (x.left != TreeNode.SENTINEL && x.lf_left + 1 >= lineNumber) {
                    x = x.left;
                } else if (x.lf_left + x.piece.lineFeedCnt + 1 >= lineNumber) {
                    leftLen += x.size_left;
                    // lineNumber >= 2
                    var accumualtedValInCurrentIndex = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 2);
                    return leftLen += accumualtedValInCurrentIndex + column - 1;
                } else {
                    lineNumber -= x.lf_left + x.piece.lineFeedCnt;
                    leftLen += x.size_left + x.piece.length;
                    x = x.right;
                }
            }

            return leftLen;
        }

        public TextPosition GetPositionAt(int offset) {
            offset = Math.Max(0, offset);

            var x = this.root;
            var lfCnt = 0;
            var originalOffset = offset;

            while (x != TreeNode.SENTINEL) {
                if (x.size_left != 0 && x.size_left >= offset) {
                    x = x.left;
                } else if (x.size_left + x.piece.length >= offset) {
                    var o = this.GetIndexOf(x, offset - x.size_left);

                    lfCnt += x.lf_left + o.index;

                    if (o.index == 0) {
                        var lineStartOffset = this.GetOffsetAt(lfCnt + 1, 1);
                        var column = originalOffset - lineStartOffset;
                        return new TextPosition(lfCnt + 1, column + 1);
                    }

                    return new TextPosition(lfCnt + 1, o.remainder + 1);
                } else {
                    offset -= x.size_left + x.piece.length;
                    lfCnt += x.lf_left + x.piece.lineFeedCnt;

                    if (x.right == TreeNode.SENTINEL) {
                        // last node
                        var lineStartOffset = this.GetOffsetAt(lfCnt + 1, 1);
                        var column = originalOffset - offset - lineStartOffset;
                        return new TextPosition(lfCnt + 1, column + 1);
                    } else {
                        x = x.right;
                    }
                }
            }

            return new TextPosition(1, 1);
        }

        public string GetValueInRange(TextPosition start, TextPosition end, string eol = null) {
            if (start.line == end.line && start.column == end.column) {
                return "";
            }

            var startPosition = this.NodeAt2(start.line, start.column);
            var endPosition = this.NodeAt2(end.line, end.column);

            var value = this.GetValueInRange(startPosition.Value, endPosition.Value);
            if (eol != null) {
                if (eol != this._EOL || !this._EOLNormalized) {
                    return Regex.Replace(value, @"\r\n|\r|\n", eol);
                }

                if (eol == this.EOL && this._EOLNormalized) {
                    if (eol == "\r\n") {

                    }
                    return value;
                }
                return Regex.Replace(value, @"\r\n|\r|\n", eol);
            }
            return value;
        }

        private string GetValueInRange(NodePosition startPosition, NodePosition endPosition) {
            if (startPosition.node == endPosition.node) {
                var node = startPosition.node;
                var buffer0 = this._buffers[node.piece.bufferIndex].buffer;
                var startOffset0 = this.OffsetInBuffer(node.piece.bufferIndex, node.piece.start);
                return buffer0.Substring(startOffset0 + startPosition.remainder, endPosition.remainder - startPosition.remainder);
            }

            var x = startPosition.node;
            var buffer = this._buffers[x.piece.bufferIndex].buffer;
            var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);
            var ret = buffer.Substring(startOffset + startPosition.remainder, x.piece.length - startPosition.remainder);

            x = x.Next();
            while (x != TreeNode.SENTINEL) {
                buffer = this._buffers[x.piece.bufferIndex].buffer;
                startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);

                if (x == endPosition.node) {
                    ret += buffer.Substring(startOffset, endPosition.remainder);
                    break;
                } else {
                    ret += buffer.Substring(startOffset, x.piece.length);
                }

                x = x.Next();
            }

            return ret;
        }

        public List<string> GetLinesContent() {
            var lines = new List<string>();
            var currentLine = "";
            var danglingCR = false;

            this.Iterate(this.root, node => {
                if (node == TreeNode.SENTINEL) {
                    return true;
                }

                var piece = node.piece;
                var pieceLength = piece.length;
                if (pieceLength == 0) {
                    return true;
                }

                var buffer = this._buffers[piece.bufferIndex].buffer;
                var lineStarts = this._buffers[piece.bufferIndex].lineStarts;

                var pieceStartLine = piece.start.line;
                var pieceEndLine = piece.end.line;
                var pieceStartOffset = lineStarts[pieceStartLine] + piece.start.column;

                if (danglingCR) {
                    if (buffer[pieceStartOffset] == '\n') {
                        // pretend the \n was in the previous piece..
                        pieceStartOffset++;
                        pieceLength--;
                    }
                    lines.Add(currentLine);
                    currentLine = "";
                    danglingCR = false;
                    if (pieceLength == 0) {
                        return true;
                    }
                }

                if (pieceStartLine == pieceEndLine) {
                    // this piece has no new lines
                    if (!this._EOLNormalized && buffer[pieceStartOffset + pieceLength - 1] == '\r') {
                        danglingCR = true;
                        currentLine += buffer.Substring(pieceStartOffset, pieceLength - 1);
                    } else {
                        currentLine += buffer.Substring(pieceStartOffset, pieceLength);
                    }
                    return true;
                }

                // add the text before the first line start in this piece
                currentLine += (
                    this._EOLNormalized
                        ? buffer.Substring(pieceStartOffset, Math.Max(pieceStartOffset, lineStarts[pieceStartLine + 1] - this._EOLLength) - pieceStartOffset)
                        : Regex.Replace(buffer.Substring(pieceStartOffset, lineStarts[pieceStartLine + 1] - pieceStartOffset), @"(\r\n|\r|\n)$", "")
                );
                lines.Add(currentLine);

                for (var line = pieceStartLine + 1; line < pieceEndLine; line++) {
                    currentLine = (
                        this._EOLNormalized
                            ? buffer.Substring(lineStarts[line], lineStarts[line + 1] - this._EOLLength - lineStarts[line])
                            : Regex.Replace(buffer.Substring(lineStarts[line], lineStarts[line + 1] - lineStarts[line]), @"(\r\n|\r|\n)$", "")
                    );
                    lines.Add(currentLine);
                }

                if (!this._EOLNormalized && buffer[lineStarts[pieceEndLine] + piece.end.column - 1] == '\r') {
                    danglingCR = true;
                    if (piece.end.column == 0) {
                        // The last line ended with a \r, let's undo the push, it will be pushed by next iteration
                        lines.RemoveAt(lines.Count - 1);
                    } else {
                        currentLine = buffer.Substring(lineStarts[pieceEndLine], piece.end.column - 1);
                    }
                } else {
                    currentLine = buffer.Substring(lineStarts[pieceEndLine], piece.end.column);
                }

                return true;
            });

            if (danglingCR) {
                lines.Add(currentLine);
                currentLine = "";
            }

            lines.Add(currentLine);
            return lines;
        }

        public int Length => this._length;

        public int LineCount => this._lineCnt;

        public string GetLineContent(int lineNumber) {
            if (this._lastVisitedLineNumber == lineNumber) {
                return this._lastVisitedLine;
            }

            this._lastVisitedLineNumber = lineNumber;

            if (lineNumber == this._lineCnt) {
                this._lastVisitedLine = this.GetLineRawContent(lineNumber);
            } else if (this._EOLNormalized) {
                this._lastVisitedLine = this.GetLineRawContent(lineNumber, this._EOLLength);
            } else {
                this._lastVisitedLine = Regex.Replace(this.GetLineRawContent(lineNumber), @"(\r\n|\r|\n)$", "");
            }

            return this._lastVisitedLine;
        }

        private char _GetCharCode(NodePosition nodePos) {
            if (nodePos.remainder == nodePos.node.piece.length) {
                // the char we want to fetch is at the head of next node.
                var matchingNode = nodePos.node.Next();
                if (matchingNode == null) {
                    return '\0';
                }

                var buffer = this._buffers[matchingNode.piece.bufferIndex];
                var startOffset = this.OffsetInBuffer(matchingNode.piece.bufferIndex, matchingNode.piece.start);
                return buffer.buffer[startOffset];
            } else {
                var buffer = this._buffers[nodePos.node.piece.bufferIndex];
                var startOffset = this.OffsetInBuffer(nodePos.node.piece.bufferIndex, nodePos.node.piece.start);
                var targetOffset = startOffset + nodePos.remainder;

                return buffer.buffer[targetOffset];
            }
        }

        public char GetLineCharCode(int lineNumber, int index) {
            var nodePos = this.NodeAt2(lineNumber, index + 1);
            return this._GetCharCode(nodePos.Value);
        }

        public int GetLineLength(int lineNumber) {
            if (lineNumber == this.LineCount) {
                var startOffset = this.GetOffsetAt(lineNumber, 1);
                return this.Length - startOffset;
            }
            return this.GetOffsetAt(lineNumber + 1, 1) - this.GetOffsetAt(lineNumber, 1) - this._EOLLength;
        }

        public char this[int offset]
        {
            get
            {
                var nodePos = this.NodeAt(offset);
                return this._GetCharCode(nodePos.Value);
            }
        }
/*
        public int findMatchesInNode(TreeNode node, Searcher searcher, int startLineNumber, int startColumn, BufferCursor startCursor, BufferCursor endCursor, SearchData searchData, bool captureMatches, int limitResultCount, int resultLen, FindMatch result[]) {
            var buffer = this._buffers[node.piece.bufferIndex];
            var startOffsetInBuffer = this.OffsetInBuffer(node.piece.bufferIndex, node.piece.start);
            var start = this.OffsetInBuffer(node.piece.bufferIndex, startCursor);
            var end = this.OffsetInBuffer(node.piece.bufferIndex, endCursor);

            RegExpExecArray m;
            // Reset regex to search from the beginning
            BufferCursor ret = new BufferCursor(0, 0);
            string searchText;
            offsetInBuffer: (number offset) => number;

            if (searcher._wordSeparators) {
                searchText = buffer.buffer.substring(start, end);
                offsetInBuffer = (number offset) => offset + start;
                searcher.reset(0);
            } else {
                searchText = buffer.buffer;
                offsetInBuffer = (number offset) => offset;
                searcher.reset(start);
            }

            do {
                m = searcher.next(searchText);

                if (m) {
                    if (offsetInBuffer(m.index) >= end) {
                        return resultLen;
                    }
                    this.positionInBuffer(node, offsetInBuffer(m.index) - startOffsetInBuffer, ret);
                    var lineFeedCnt = this.getLineFeedCnt(node.piece.bufferIndex, startCursor, ret);
                    var retStartColumn = ret.line == startCursor.line ? ret.column - startCursor.column + startColumn : ret.column + 1;
                    var retEndColumn = retStartColumn + m[0].length;
                    result[resultLen++] = createFindMatch(new Range(startLineNumber + lineFeedCnt, retStartColumn, startLineNumber + lineFeedCnt, retEndColumn), m, captureMatches);

                    if (offsetInBuffer(m.index) + m[0].length >= end) {
                        return resultLen;
                    }
                    if (resultLen >= limitResultCount) {
                        return resultLen;
                    }
                }

            } while (m);

            return resultLen;
        }

        public FindMatch[] findMatchesLineByLine(Range searchRange, SearchData searchData, bool captureMatches, number limitResultCount) {
            var result = new FindMatch[];
            var resultLen = 0;
            var searcher = new Searcher(searchData.wordSeparators, searchData.regex);

            var startPosition = this.NodeAt2(searchRange.startLineNumber, searchRange.startColumn);
            if (startPosition == null) {
                return [];
            }
            var endPosition = this.NodeAt2(searchRange.endLineNumber, searchRange.endColumn);
            if (endPosition == null) {
                return [];
            }
            var start = this.positionInBuffer(startPosition.node, startPosition.remainder);
            var end = this.positionInBuffer(endPosition.node, endPosition.remainder);

            if (startPosition.node == endPosition.node) {
                this.findMatchesInNode(startPosition.node, searcher, searchRange.startLineNumber, searchRange.startColumn, start, end, searchData, captureMatches, limitResultCount, resultLen, result);
                return result;
            }

            var startLineNumber = searchRange.startLineNumber;

            var currentNode = startPosition.node;
            while (currentNode != endPosition.node) {
                var lineBreakCnt = this.getLineFeedCnt(currentNode.piece.bufferIndex, start, currentNode.piece.end);

                if (lineBreakCnt >= 1) {
                    // last line break position
                    var lineStarts = this._buffers[currentNode.piece.bufferIndex].lineStarts;
                    var startOffsetInBuffer = this.OffsetInBuffer(currentNode.piece.bufferIndex, currentNode.piece.start);
                    var nextLineStartOffset = lineStarts[start.line + lineBreakCnt];
                    var startColumn = startLineNumber == searchRange.startLineNumber ? searchRange.startColumn : 1;
                    resultLen = this.findMatchesInNode(currentNode, searcher, startLineNumber, startColumn, start, this.positionInBuffer(currentNode, nextLineStartOffset - startOffsetInBuffer), searchData, captureMatches, limitResultCount, resultLen, result);

                    if (resultLen >= limitResultCount) {
                        return result;
                    }

                    startLineNumber += lineBreakCnt;
                }

                var startColumn = startLineNumber == searchRange.startLineNumber ? searchRange.startColumn - 1 : 0;
                // search for the remaining content
                if (startLineNumber == searchRange.endLineNumber) {
                    const text = this.getLineContent(startLineNumber).substring(startColumn, searchRange.endColumn - 1);
                    resultLen = this._findMatchesInLine(searchData, searcher, text, searchRange.endLineNumber, startColumn, resultLen, result, captureMatches, limitResultCount);
                    return result;
                }

                resultLen = this._findMatchesInLine(searchData, searcher, this.getLineContent(startLineNumber).substr(startColumn), startLineNumber, startColumn, resultLen, result, captureMatches, limitResultCount);

                if (resultLen >= limitResultCount) {
                    return result;
                }

                startLineNumber++;
                startPosition = this.NodeAt2(startLineNumber, 1);
                currentNode = startPosition.node;
                start = this.positionInBuffer(startPosition.node, startPosition.remainder);
            }

            if (startLineNumber == searchRange.endLineNumber) {
                var startColumn = startLineNumber == searchRange.startLineNumber ? searchRange.startColumn - 1 : 0;
                const text = this.getLineContent(startLineNumber).substring(startColumn, searchRange.endColumn - 1);
                resultLen = this._findMatchesInLine(searchData, searcher, text, searchRange.endLineNumber, startColumn, resultLen, result, captureMatches, limitResultCount);
                return result;
            }

            var startColumn = startLineNumber == searchRange.startLineNumber ? searchRange.startColumn : 1;
            resultLen = this.findMatchesInNode(endPosition.node, searcher, startLineNumber, startColumn, start, end, searchData, captureMatches, limitResultCount, resultLen, result);
            return result;
        }

        private int _findMatchesInLine(SearchData searchData, Searcher searcher, string text, number lineNumber, number deltaOffset, number resultLen, FindMatch result[], bool captureMatches, number limitResultCount) {
            const wordSeparators = searchData.wordSeparators;
            if (!captureMatches && searchData.simpleSearch) {
                const searchString = searchData.simpleSearch;
                const searchStringLen = searchString.length;
                const textLength = text.length;

                var lastMatchIndex = -searchStringLen;
                while ((lastMatchIndex = text.indexOf(searchString, lastMatchIndex + searchStringLen)) != -1) {
                    if (!wordSeparators || isValidMatch(wordSeparators, text, textLength, lastMatchIndex, searchStringLen)) {
                        result[resultLen++] = new FindMatch(new Range(lineNumber, lastMatchIndex + 1 + deltaOffset, lineNumber, lastMatchIndex + 1 + searchStringLen + deltaOffset), null);
                        if (resultLen >= limitResultCount) {
                            return resultLen;
                        }
                    }
                }
                return resultLen;
            }

            let RegExpExecArray m;
            // Reset regex to search from the beginning
            searcher.reset(0);
            do {
                m = searcher.next(text);
                if (m) {
                    result[resultLen++] = createFindMatch(new Range(lineNumber, m.index + 1 + deltaOffset, lineNumber, m.index + 1 + m[0].length + deltaOffset), m, captureMatches);
                    if (resultLen >= limitResultCount) {
                        return resultLen;
                    }
                }
            } while (m);
            return resultLen;
        }
*/

        // #endregion

        // #region Piece Table
        public void Insert(int offset, string value, bool eolNormalized = false) {
            this._EOLNormalized = this._EOLNormalized && eolNormalized;
            this._lastVisitedLineNumber = 0;
            this._lastVisitedLine = "";

            if (this.root != TreeNode.SENTINEL) {
                var position = this.NodeAt(offset).Value;
                var piece = position.node.piece;
                var bufferIndex = piece.bufferIndex;
                var insertPosInBuffer = this.PositionInBuffer(position.node, position.remainder);
                if (position.node.piece.bufferIndex == 0 &&
                    piece.end.line == this._lastChangeBufferPos.line &&
                    piece.end.column == this._lastChangeBufferPos.column &&
                    (position.nodeStartOffset + piece.length == offset) &&
                    value.Length < AverageBufferSize
                ) {
                    // changed buffer
                    this.AppendToNode(position.node, value);
                    this.ComputeBufferMetadata();
                    return;
                }

                if (position.nodeStartOffset == offset) {
                    this.InsertContentToNodeLeft(value, position.node);
                    this._searchCache.Validate(offset);
                } else if (position.nodeStartOffset + position.node.piece.length > offset) {
                    // we are inserting into the middle of a node.
                    var nodesToDel = new List<TreeNode>();
                    var newRightPiece = new Piece(
                        piece.bufferIndex,
                        insertPosInBuffer,
                        piece.end,
                        this.GetLineFeedCnt(piece.bufferIndex, insertPosInBuffer, piece.end),
                        this.OffsetInBuffer(bufferIndex, piece.end) - this.OffsetInBuffer(bufferIndex, insertPosInBuffer)
                    );

                    if (this.ShouldCheckCRLF() && this.EndWithCR(value)) {
                        var headOfRight = this.NodeCharCodeAt(position.node, position.remainder);

                        if (headOfRight == '\n') {
                            var newStart = new TextPosition(newRightPiece.start.line + 1, 0);
                            newRightPiece = new Piece(
                                newRightPiece.bufferIndex,
                                newStart,
                                newRightPiece.end,
                                this.GetLineFeedCnt(newRightPiece.bufferIndex, newStart, newRightPiece.end),
                                newRightPiece.length - 1
                            );

                            value += "\n";
                        }
                    }

                    // reuse node for content before insertion point.
                    if (this.ShouldCheckCRLF() && this.StartWithLF(value)) {
                        var tailOfLeft = this.NodeCharCodeAt(position.node, position.remainder - 1);
                        if (tailOfLeft == '\r') {
                            var previousPos = this.PositionInBuffer(position.node, position.remainder - 1);
                            this.DeleteNodeTail(position.node, previousPos);
                            value = "\r" + value;

                            if (position.node.piece.length == 0) {
                                nodesToDel.Add(position.node);
                            }
                        } else {
                            this.DeleteNodeTail(position.node, insertPosInBuffer);
                        }
                    } else {
                        this.DeleteNodeTail(position.node, insertPosInBuffer);
                    }

                    var newPieces = this.CreateNewPieces(value);
                    if (newRightPiece.length > 0) {
                        this.RbInsertRight(position.node, newRightPiece);
                    }

                    var tmpNode = position.node;
                    for (var k = 0; k < newPieces.Count; k++) {
                        tmpNode = this.RbInsertRight(tmpNode, newPieces[k]);
                    }
                    this.DeleteNodes(nodesToDel);
                } else {
                    this.InsertContentToNodeRight(value, position.node);
                }
            } else {
                // insert new node
                var pieces = this.CreateNewPieces(value);
                var node = this.RbInsertLeft(null, pieces[0]);

                for (var k = 1; k < pieces.Count; k++) {
                    node = this.RbInsertRight(node, pieces[k]);
                }
            }

            // todo, this is too brutal. Total line feed count should be updated the same way as lf_left.
            this.ComputeBufferMetadata();
        }

        public void Delete(int offset, int cnt) {
            this._lastVisitedLineNumber = 0;
            this._lastVisitedLine = "";

            if (cnt <= 0 || this.root == TreeNode.SENTINEL) {
                return;
            }

            var startPosition = this.NodeAt(offset).Value;
            var endPosition = this.NodeAt(offset + cnt).Value;
            var startNode = startPosition.node;
            var endNode = endPosition.node;

            if (startNode == endNode) {
                var startSplitPosInBuffer0 = this.PositionInBuffer(startNode, startPosition.remainder);
                var endSplitPosInBuffer0 = this.PositionInBuffer(startNode, endPosition.remainder);

                if (startPosition.nodeStartOffset == offset) {
                    if (cnt == startNode.piece.length) { // delete node
                        var next = startNode.Next();
                        this.RbDelete(startNode);
                        this.ValidateCRLFWithPrevNode(next);
                        this.ComputeBufferMetadata();
                        return;
                    }
                    this.DeleteNodeHead(startNode, endSplitPosInBuffer0);
                    this._searchCache.Validate(offset);
                    this.ValidateCRLFWithPrevNode(startNode);
                    this.ComputeBufferMetadata();
                    return;
                }

                if (startPosition.nodeStartOffset + startNode.piece.length == offset + cnt) {
                    this.DeleteNodeTail(startNode, startSplitPosInBuffer0);
                    this.ValidateCRLFWithNextNode(startNode);
                    this.ComputeBufferMetadata();
                    return;
                }

                // delete content in the middle, this node will be splitted to nodes
                this.ShrinkNode(startNode, startSplitPosInBuffer0, endSplitPosInBuffer0);
                this.ComputeBufferMetadata();
                return;
            }

            var nodesToDel = new List<TreeNode>();

            var startSplitPosInBuffer = this.PositionInBuffer(startNode, startPosition.remainder);
            this.DeleteNodeTail(startNode, startSplitPosInBuffer);
            this._searchCache.Validate(offset);
            if (startNode.piece.length == 0) {
                nodesToDel.Add(startNode);
            }

            // update last touched node
            var endSplitPosInBuffer = this.PositionInBuffer(endNode, endPosition.remainder);
            this.DeleteNodeHead(endNode, endSplitPosInBuffer);
            if (endNode.piece.length == 0) {
                nodesToDel.Add(endNode);
            }

            // delete nodes in between
            var secondNode = startNode.Next();
            for (var node = secondNode; node != TreeNode.SENTINEL && node != endNode; node = node.Next()) {
                nodesToDel.Add(node);
            }

            var prev = startNode.piece.length == 0 ? startNode.Prev() : startNode;
            this.DeleteNodes(nodesToDel);
            this.ValidateCRLFWithNextNode(prev);
            this.ComputeBufferMetadata();
        }

        private void InsertContentToNodeLeft(string value, TreeNode node) {
            // we are inserting content to the beginning of node
            var nodesToDel = new List<TreeNode>();
            if (this.ShouldCheckCRLF() && this.EndWithCR(value) && this.StartWithLF(node)) {
                // move `\n` to new node.

                var piece = node.piece;
                var newStart = new TextPosition(piece.start.line + 1, 0);
                var nPiece = new Piece(
                    piece.bufferIndex,
                    newStart,
                    piece.end,
                    this.GetLineFeedCnt(piece.bufferIndex, newStart, piece.end),
                    piece.length - 1
                );

                node.piece = nPiece;

                value += "\n";
                this.UpdateTreeMetadata(node, -1, -1);

                if (node.piece.length == 0) {
                    nodesToDel.Add(node);
                }
            }

            var newPieces = this.CreateNewPieces(value);
            var newNode = this.RbInsertLeft(node, newPieces[newPieces.Count - 1]);
            for (var k = newPieces.Count - 2; k >= 0; k--) {
                newNode = this.RbInsertLeft(newNode, newPieces[k]);
            }
            this.ValidateCRLFWithPrevNode(newNode);
            this.DeleteNodes(nodesToDel);
        }

        private void InsertContentToNodeRight(string value, TreeNode node) {
            // we are inserting to the right of this node.
            if (this.AdjustCarriageReturnFromNext(value, node)) {
                // move \n to the new node.
                value += "\n";
            }

            var newPieces = this.CreateNewPieces(value);
            var newNode = this.RbInsertRight(node, newPieces[0]);
            var tmpNode = newNode;

            for (var k = 1; k < newPieces.Count; k++) {
                tmpNode = this.RbInsertRight(tmpNode, newPieces[k]);
            }

            this.ValidateCRLFWithPrevNode(newNode);
        }

        private TextPosition PositionInBuffer(TreeNode node, int remainder) {
            var piece = node.piece;
            var bufferIndex = node.piece.bufferIndex;
            var lineStarts = this._buffers[bufferIndex].lineStarts;

            var startOffset = lineStarts[piece.start.line] + piece.start.column;

            var offset = startOffset + remainder;

            // binary search offset between startOffset and endOffset
            var low = piece.start.line;
            var high = piece.end.line;

            var mid = 0;
            var midStop = 0;
            var midStart = 0;

            while (low <= high) {
                mid = low + ((high - low) / 2) | 0;
                midStart = lineStarts[mid];

                if (mid == high) {
                    break;
                }

                midStop = lineStarts[mid + 1];

                if (offset < midStart) {
                    high = mid - 1;
                } else if (offset >= midStop) {
                    low = mid + 1;
                } else {
                    break;
                }
            }

            return new TextPosition(
                line: mid,
                column: offset - midStart
            );
        }

        private int GetLineFeedCnt(int bufferIndex, TextPosition start, TextPosition end) {
            // we don't need to worry about abc start\r|\n, or abc|\r, or abc|\n, or abc|\r\n doesn't change the fact that, there is one line break after start.
            // now let's take care of abc end\r|\n, if end is in between \r and \n, we need to add line feed count by 1
            if (end.column == 0) {
                return end.line - start.line;
            }

            var lineStarts = this._buffers[bufferIndex].lineStarts;
            if (end.line == lineStarts.Length - 1) { // it means, there is no \n after end, otherwise, there will be one more lineStart.
                return end.line - start.line;
            }

            var nextLineStartOffset = lineStarts[end.line + 1];
            var endOffset = lineStarts[end.line] + end.column;
            if (nextLineStartOffset > endOffset + 1) { // there are more than 1 character after end, which means it can't be \n
                return end.line - start.line;
            }
            // endOffset + 1 == nextLineStartOffset
            // character at endOffset is \n, so we check the character before first
            // if character at endOffset is \r, end.column is 0 and we can't get here.
            var previousCharOffset = endOffset - 1; // end.column > 0 so it's okay.
            var buffer = this._buffers[bufferIndex].buffer;

            if (buffer[previousCharOffset] == '\r') {
                return end.line - start.line + 1;
            } else {
                return end.line - start.line;
            }
        }

        private int OffsetInBuffer(int bufferIndex, TextPosition cursor) {
            var lineStarts = this._buffers[bufferIndex].lineStarts;
            return lineStarts[cursor.line] + cursor.column;
        }

        private void DeleteNodes(List<TreeNode> nodes) {
            for (var i = 0; i < nodes.Count; i++) {
                this.RbDelete(nodes[i]);
            }
        }

        private List<Piece> CreateNewPieces(string text) {
            if (text.Length > AverageBufferSize) {
                // the content is large, operations like substring, charCode becomes slow
                // so here we split it into smaller chunks, just like what we did for CR/LF normalization
                var newPieces = new List<Piece>();
                while (text.Length > AverageBufferSize) {
                    var lastChar = text[AverageBufferSize - 1];
                    string splitText;
                    if (lastChar == '\r' || (lastChar >= 0xD800 && lastChar <= 0xDBFF)) {
                        // last character is \r or a high surrogate => keep it back
                        splitText = text.Substring(0, AverageBufferSize - 1);
                        text = text.Substring(AverageBufferSize - 1);
                    } else {
                        splitText = text.Substring(0, AverageBufferSize);
                        text = text.Substring(AverageBufferSize);
                    }

                    var lineStarts0 = LineStarts.CreateLineStartsFast(splitText);
                    newPieces.Add(new Piece(
                        this._buffers.Count, /* buffer index */
                        new TextPosition(0, 0),
                        new TextPosition(lineStarts0.Length - 1, splitText.Length - lineStarts0[lineStarts0.Length - 1]),
                        lineStarts0.Length - 1,
                        splitText.Length
                    ));
                    this._buffers.Add(new StringBuffer(splitText, lineStarts0));
                }

                var lineStarts1 = LineStarts.CreateLineStartsFast(text);
                newPieces.Add(new Piece(
                    this._buffers.Count, /* buffer index */
                    new TextPosition(0, 0),
                    new TextPosition(lineStarts1.Length - 1, text.Length - lineStarts1[lineStarts1.Length - 1]),
                    lineStarts1.Length - 1,
                    text.Length
                ));
                this._buffers.Add(new StringBuffer(text, lineStarts1));

                return newPieces;
            }

            var startOffset = this._buffers[0].buffer.Length;
            var lineStarts = LineStarts.CreateLineStartsFast(text);

            var start = this._lastChangeBufferPos;
            if (this._buffers[0].lineStarts[this._buffers[0].lineStarts.Length - 1] == startOffset
                && startOffset != 0
                && this.StartWithLF(text)
                && this.EndWithCR(this._buffers[0].buffer) // todo, we can check this._lastChangeBufferPos's column as it's the last one
            ) {
                this._lastChangeBufferPos = new TextPosition(this._lastChangeBufferPos.line, this._lastChangeBufferPos.column + 1);
                start = this._lastChangeBufferPos;

                for (var i = 0; i < lineStarts.Length; i++) {
                    lineStarts[i] += startOffset + 1;
                }

                this._buffers[0].lineStarts = this._buffers[0].lineStarts.Concat(lineStarts.Skip(1)).ToArray();
                this._buffers[0].buffer += '_' + text;
                startOffset += 1;
            } else {
                if (startOffset != 0) {
                    for (var i = 0; i < lineStarts.Length; i++) {
                        lineStarts[i] += startOffset;
                    }
                }
                this._buffers[0].lineStarts = this._buffers[0].lineStarts.Concat(lineStarts.Skip(1)).ToArray();
                this._buffers[0].buffer += text;
            }

            var endOffset = this._buffers[0].buffer.Length;
            var endIndex = this._buffers[0].lineStarts.Length - 1;
            var endColumn = endOffset - this._buffers[0].lineStarts[endIndex];
            var endPos = new TextPosition(endIndex, endColumn);
            var newPiece = new Piece(
                0, /** todo@peng */
                start,
                endPos,
                this.GetLineFeedCnt(0, start, endPos),
                endOffset - startOffset
            );
            this._lastChangeBufferPos = endPos;
            return new List<Piece>() { newPiece };
        }

        public string GetLinesRawContent() {
            return this.GetContentOfSubTree(this.root);
        }

        public string GetLineRawContent(int lineNumber, int endOffset = 0) {
            var x = this.root;

            var ret = "";
            var cache = this._searchCache.Get2(lineNumber);
            if (cache != null) {
                x = cache.node;
                var prevAccumulatedValue = this.GetAccumulatedValue(x, lineNumber - cache.nodeStartLineNumber.Value - 1);
                var buffer = this._buffers[x.piece.bufferIndex].buffer;
                var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);
                if (cache.nodeStartLineNumber + x.piece.lineFeedCnt == lineNumber) {
                    ret = buffer.Substring(startOffset + prevAccumulatedValue, x.piece.length - prevAccumulatedValue);
                } else {
                    var accumulatedValue = this.GetAccumulatedValue(x, lineNumber - cache.nodeStartLineNumber.Value);
                    return buffer.Substring(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue);
                }
            } else {
                var nodeStartOffset = 0;
                var originalLineNumber = lineNumber;
                while (x != TreeNode.SENTINEL) {
                    if (x.left != TreeNode.SENTINEL && x.lf_left >= lineNumber - 1) {
                        x = x.left;
                    } else if (x.lf_left + x.piece.lineFeedCnt > lineNumber - 1) {
                        var prevAccumulatedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 2);
                        var accumulatedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 1);
                        var buffer = this._buffers[x.piece.bufferIndex].buffer;
                        var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);
                        nodeStartOffset += x.size_left;
                        this._searchCache.Set(new CacheEntry(
                            node: x,
                            nodeStartOffset: nodeStartOffset,
                            nodeStartLineNumber: originalLineNumber - (lineNumber - 1 - x.lf_left)
                        ));
                        return buffer.Substring(startOffset + prevAccumulatedValue, accumulatedValue - endOffset - prevAccumulatedValue);
                    } else if (x.lf_left + x.piece.lineFeedCnt == lineNumber - 1) {
                        var prevAccumulatedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 2);
                        var buffer = this._buffers[x.piece.bufferIndex].buffer;
                        var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);

                        ret = buffer.Substring(startOffset + prevAccumulatedValue, x.piece.length - prevAccumulatedValue);
                        break;
                    } else {
                        lineNumber -= x.lf_left + x.piece.lineFeedCnt;
                        nodeStartOffset += x.size_left + x.piece.length;
                        x = x.right;
                    }
                }
            }

            // search in order, to find the node contains end column
            x = x.Next();
            while (x != TreeNode.SENTINEL) {
                var buffer = this._buffers[x.piece.bufferIndex].buffer;

                if (x.piece.lineFeedCnt > 0) {
                    var accumulatedValue = this.GetAccumulatedValue(x, 0);
                    var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);

                    ret += buffer.Substring(startOffset, accumulatedValue - endOffset);
                    return ret;
                } else {
                    var startOffset = this.OffsetInBuffer(x.piece.bufferIndex, x.piece.start);
                    ret += buffer.Substring(startOffset, x.piece.length);
                }

                x = x.Next();
            }

            return ret;
        }

        private void ComputeBufferMetadata() {
            var x = this.root;

            var lfCnt = 1;
            var len = 0;

            while (x != TreeNode.SENTINEL) {
                lfCnt += x.lf_left + x.piece.lineFeedCnt;
                len += x.size_left + x.piece.length;
                x = x.right;
            }

            this._lineCnt = lfCnt;
            this._length = len;
            this._searchCache.Validate(this._length);
        }

        private struct IndexPosition
        {
            internal readonly int index;
            internal readonly int remainder;

            internal IndexPosition(int index, int remainder)
            {
                this.index = index;
                this.remainder = remainder;
            }
        }

        // #region node operations
        private IndexPosition GetIndexOf(TreeNode node, int accumulatedValue) {
            var piece = node.piece;
            var pos = this.PositionInBuffer(node, accumulatedValue);
            var lineCnt = pos.line - piece.start.line;

            if (this.OffsetInBuffer(piece.bufferIndex, piece.end) - this.OffsetInBuffer(piece.bufferIndex, piece.start) == accumulatedValue) {
                // we are checking the end of this node, so a CRLF check is necessary.
                var realLineCnt = this.GetLineFeedCnt(node.piece.bufferIndex, piece.start, pos);
                if (realLineCnt != lineCnt) {
                    // aha yes, CRLF
                    return new IndexPosition(realLineCnt, 0);
                }
            }

            return new IndexPosition(lineCnt, pos.column);
        }

        private int GetAccumulatedValue(TreeNode node, int index) {
            if (index < 0) {
                return 0;
            }
            var piece = node.piece;
            var lineStarts = this._buffers[piece.bufferIndex].lineStarts;
            var expectedLineStartIndex = piece.start.line + index + 1;
            if (expectedLineStartIndex > piece.end.line) {
                return lineStarts[piece.end.line] + piece.end.column - lineStarts[piece.start.line] - piece.start.column;
            } else {
                return lineStarts[expectedLineStartIndex] - lineStarts[piece.start.line] - piece.start.column;
            }
        }

        private void DeleteNodeTail(TreeNode node, TextPosition pos) {
            var piece = node.piece;
            var originalLFCnt = piece.lineFeedCnt;
            var originalEndOffset = this.OffsetInBuffer(piece.bufferIndex, piece.end);

            var newEnd = pos;
            var newEndOffset = this.OffsetInBuffer(piece.bufferIndex, newEnd);
            var newLineFeedCnt = this.GetLineFeedCnt(piece.bufferIndex, piece.start, newEnd);

            var lf_delta = newLineFeedCnt - originalLFCnt;
            var size_delta = newEndOffset - originalEndOffset;
            var newLength = piece.length + size_delta;

            node.piece = new Piece(
                piece.bufferIndex,
                piece.start,
                newEnd,
                newLineFeedCnt,
                newLength
            );

            this.UpdateTreeMetadata(node, size_delta, lf_delta);
        }

        private void DeleteNodeHead(TreeNode node, TextPosition pos) {
            var piece = node.piece;
            var originalLFCnt = piece.lineFeedCnt;
            var originalStartOffset = this.OffsetInBuffer(piece.bufferIndex, piece.start);

            var newStart = pos;
            var newLineFeedCnt = this.GetLineFeedCnt(piece.bufferIndex, newStart, piece.end);
            var newStartOffset = this.OffsetInBuffer(piece.bufferIndex, newStart);
            var lf_delta = newLineFeedCnt - originalLFCnt;
            var size_delta = originalStartOffset - newStartOffset;
            var newLength = piece.length + size_delta;
            node.piece = new Piece(
                piece.bufferIndex,
                newStart,
                piece.end,
                newLineFeedCnt,
                newLength
            );

            this.UpdateTreeMetadata(node, size_delta, lf_delta);
        }

        private void ShrinkNode(TreeNode node, TextPosition start, TextPosition end) {
            var piece = node.piece;
            var originalStartPos = piece.start;
            var originalEndPos = piece.end;

            // old piece, originalStartPos, start
            var oldLength = piece.length;
            var oldLFCnt = piece.lineFeedCnt;
            var newEnd = start;
            var newLineFeedCnt = this.GetLineFeedCnt(piece.bufferIndex, piece.start, newEnd);
            var newLength = this.OffsetInBuffer(piece.bufferIndex, start) - this.OffsetInBuffer(piece.bufferIndex, originalStartPos);

            node.piece = new Piece(
                piece.bufferIndex,
                piece.start,
                newEnd,
                newLineFeedCnt,
                newLength
            );

            this.UpdateTreeMetadata(node, newLength - oldLength, newLineFeedCnt - oldLFCnt);

            // new right piece, end, originalEndPos
            var newPiece = new Piece(
                piece.bufferIndex,
                end,
                originalEndPos,
                this.GetLineFeedCnt(piece.bufferIndex, end, originalEndPos),
                this.OffsetInBuffer(piece.bufferIndex, originalEndPos) - this.OffsetInBuffer(piece.bufferIndex, end)
            );

            var newNode = this.RbInsertRight(node, newPiece);
            this.ValidateCRLFWithPrevNode(newNode);
        }

        private void AppendToNode(TreeNode node, string value) {
            if (this.AdjustCarriageReturnFromNext(value, node)) {
                value += "\n";
            }

            var hitCRLF = this.ShouldCheckCRLF() && this.StartWithLF(value) && this.EndWithCR(node);
            var startOffset = this._buffers[0].buffer.Length;
            this._buffers[0].buffer += value;
            var lineStarts = LineStarts.CreateLineStartsFast(value);
            for (var i = 0; i < lineStarts.Length; i++) {
                lineStarts[i] += startOffset;
            }
            if (hitCRLF) {
                var prevStartOffset = this._buffers[0].lineStarts[this._buffers[0].lineStarts.Length - 2];
                Array.Resize(ref this._buffers[0].lineStarts, this._buffers[0].lineStarts.Length - 1);
                // _lastChangeBufferPos is already wrong
                this._lastChangeBufferPos = new TextPosition(this._lastChangeBufferPos.line - 1, startOffset - prevStartOffset);
            }

            this._buffers[0].lineStarts = this._buffers[0].lineStarts.Concat(lineStarts.Skip(1)).ToArray();
            var endIndex = this._buffers[0].lineStarts.Length - 1;
            var endColumn = this._buffers[0].buffer.Length - this._buffers[0].lineStarts[endIndex];
            var newEnd = new TextPosition(endIndex, endColumn);
            var newLength = node.piece.length + value.Length;
            var oldLineFeedCnt = node.piece.lineFeedCnt;
            var newLineFeedCnt = this.GetLineFeedCnt(0, node.piece.start, newEnd);
            var lf_delta = newLineFeedCnt - oldLineFeedCnt;

            node.piece = new Piece(
                node.piece.bufferIndex,
                node.piece.start,
                newEnd,
                newLineFeedCnt,
                newLength
            );

            this._lastChangeBufferPos = newEnd;
            this.UpdateTreeMetadata(node, value.Length, lf_delta);
        }

        private NodePosition? NodeAt(int offset) {
            var x = this.root;
            var cache = this._searchCache.Get(offset);
            if (cache != null) {
                return new NodePosition(
                    node: cache.node,
                    remainder: offset - cache.nodeStartOffset,
                    nodeStartOffset: cache.nodeStartOffset
                );
            }

            var nodeStartOffset = 0;

            while (x != TreeNode.SENTINEL) {
                if (x.size_left > offset) {
                    x = x.left;
                } else if (x.size_left + x.piece.length >= offset) {
                    nodeStartOffset += x.size_left;
                    var ret = new NodePosition(
                        node: x,
                        remainder: offset - x.size_left,
                        nodeStartOffset: nodeStartOffset
                    );
                    this._searchCache.Set(ret);
                    return ret;
                } else {
                    offset -= x.size_left + x.piece.length;
                    nodeStartOffset += x.size_left + x.piece.length;
                    x = x.right;
                }
            }

            return null;
        }

        private NodePosition? NodeAt2(int lineNumber, int column) {
            var x = this.root;
            var nodeStartOffset = 0;

            while (x != TreeNode.SENTINEL) {
                if (x.left != TreeNode.SENTINEL && x.lf_left >= lineNumber - 1) {
                    x = x.left;
                } else if (x.lf_left + x.piece.lineFeedCnt > lineNumber - 1) {
                    var prevAccumualtedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 2);
                    var accumulatedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 1);
                    nodeStartOffset += x.size_left;

                    return new NodePosition(
                        node: x,
                        remainder: Math.Min(prevAccumualtedValue + column - 1, accumulatedValue),
                        nodeStartOffset: nodeStartOffset
                    );
                } else if (x.lf_left + x.piece.lineFeedCnt == lineNumber - 1) {
                    var prevAccumualtedValue = this.GetAccumulatedValue(x, lineNumber - x.lf_left - 2);
                    if (prevAccumualtedValue + column - 1 <= x.piece.length) {
                        return new NodePosition(
                            node: x,
                            remainder: prevAccumualtedValue + column - 1,
                            nodeStartOffset: nodeStartOffset
                        );
                    } else {
                        column -= x.piece.length - prevAccumualtedValue;
                        break;
                    }
                } else {
                    lineNumber -= x.lf_left + x.piece.lineFeedCnt;
                    nodeStartOffset += x.size_left + x.piece.length;
                    x = x.right;
                }
            }

            // search in order, to find the node contains position.column
            x = x.Next();
            while (x != TreeNode.SENTINEL) {

                if (x.piece.lineFeedCnt > 0) {
                    var accumulatedValue = this.GetAccumulatedValue(x, 0);
                    nodeStartOffset = this.OffsetOfNode(x);
                    return new NodePosition(
                        node: x,
                        remainder: Math.Min(column - 1, accumulatedValue),
                        nodeStartOffset: nodeStartOffset
                    );
                } else {
                    if (x.piece.length >= column - 1) {
                        nodeStartOffset = this.OffsetOfNode(x);
                        return new NodePosition(
                            node: x,
                            remainder: column - 1,
                            nodeStartOffset: nodeStartOffset
                        );
                    } else {
                        column -= x.piece.length;
                    }
                }

                x = x.Next();
            }

            return null;
        }

        private char NodeCharCodeAt(TreeNode node, int offset) {
            if (node.piece.lineFeedCnt < 1) {
                return '\0';
            }
            var buffer = this._buffers[node.piece.bufferIndex];
            var newOffset = this.OffsetInBuffer(node.piece.bufferIndex, node.piece.start) + offset;
            return buffer.buffer[newOffset];
        }

        private int OffsetOfNode(TreeNode node) {
            if (node == null) {
                return 0;
            }
            var pos = node.size_left;
            while (node != this.root) {
                if (node.parent.right == node) {
                    pos += node.parent.size_left + node.parent.piece.length;
                }

                node = node.parent;
            }

            return pos;
        }

        // #endregion

        // #region CRLF
        private bool ShouldCheckCRLF() {
            return !(this._EOLNormalized && this._EOL == "\n");
        }

        private bool StartWithLF(string val)
        {
            return val.Length > 0 && val[0] == '\n';
        }

        private bool StartWithLF(TreeNode val) {
            if (val == TreeNode.SENTINEL || val.piece.lineFeedCnt == 0) {
                return false;
            }

            var piece = val.piece;
            var lineStarts = this._buffers[piece.bufferIndex].lineStarts;
            var line = piece.start.line;
            var startOffset = lineStarts[line] + piece.start.column;
            if (line == lineStarts.Length - 1) {
                // last line, so there is no line feed at the end of this line
                return false;
            }
            var nextLineOffset = lineStarts[line + 1];
            if (nextLineOffset > startOffset + 1) {
                return false;
            }
            return this._buffers[piece.bufferIndex].buffer[startOffset] == '\n';
        }

        private bool EndWithCR(string val)
        {
            return val.Length > 0 && val[val.Length - 1] == '\r';
        }

        private bool EndWithCR(TreeNode val) {
            if (val == TreeNode.SENTINEL || val.piece.lineFeedCnt == 0) {
                return false;
            }

            return this.NodeCharCodeAt(val, val.piece.length - 1) == '\r';
        }

        private void ValidateCRLFWithPrevNode(TreeNode nextNode) {
            if (this.ShouldCheckCRLF() && this.StartWithLF(nextNode)) {
                var node = nextNode.Prev();
                if (this.EndWithCR(node)) {
                    this.FixCRLF(node, nextNode);
                }
            }
        }

        private void ValidateCRLFWithNextNode(TreeNode node) {
            if (this.ShouldCheckCRLF() && this.EndWithCR(node)) {
                var nextNode = node.Next();
                if (this.StartWithLF(nextNode)) {
                    this.FixCRLF(node, nextNode);
                }
            }
        }

        private void FixCRLF(TreeNode prev, TreeNode next) {
            var nodesToDel = new List<TreeNode>();
            // update node
            var lineStarts = this._buffers[prev.piece.bufferIndex].lineStarts;
            TextPosition newEnd;
            if (prev.piece.end.column == 0) {
                // it means, last line ends with \r, not \r\n
                newEnd = new TextPosition(prev.piece.end.line - 1, lineStarts[prev.piece.end.line] - lineStarts[prev.piece.end.line - 1] - 1);
            } else {
                // \r\n
                newEnd = new TextPosition(prev.piece.end.line, prev.piece.end.column - 1);
            }

            var prevNewLength = prev.piece.length - 1;
            var prevNewLFCnt = prev.piece.lineFeedCnt - 1;
            prev.piece = new Piece(
                prev.piece.bufferIndex,
                prev.piece.start,
                newEnd,
                prevNewLFCnt,
                prevNewLength
            );

            this.UpdateTreeMetadata(prev, - 1, -1);
            if (prev.piece.length == 0) {
                nodesToDel.Add(prev);
            }

            // update nextNode
            var newStart = new TextPosition(next.piece.start.line + 1, 0);
            var newLength = next.piece.length - 1;
            var newLineFeedCnt = this.GetLineFeedCnt(next.piece.bufferIndex, newStart, next.piece.end);
            next.piece = new Piece(
                next.piece.bufferIndex,
                newStart,
                next.piece.end,
                newLineFeedCnt,
                newLength
            );

            this.UpdateTreeMetadata(next, - 1, -1);
            if (next.piece.length == 0) {
                nodesToDel.Add(next);
            }

            // create new piece which contains \r\n
            var pieces = this.CreateNewPieces("\r\n");
            this.RbInsertRight(prev, pieces[0]);
            // delete empty nodes

            for (var i = 0; i < nodesToDel.Count; i++) {
                this.RbDelete(nodesToDel[i]);
            }
        }

        private bool AdjustCarriageReturnFromNext(string value, TreeNode node) {
            if (this.ShouldCheckCRLF() && this.EndWithCR(value)) {
                var nextNode = node.Next();
                if (this.StartWithLF(nextNode)) {
                    // move `\n` forward
                    value += "\n";

                    if (nextNode.piece.length == 1) {
                        this.RbDelete(nextNode);
                    } else {

                        var piece = nextNode.piece;
                        var newStart = new TextPosition(piece.start.line + 1, 0);
                        var newLength = piece.length - 1;
                        var newLineFeedCnt = this.GetLineFeedCnt(piece.bufferIndex, newStart, piece.end);
                        nextNode.piece = new Piece(
                            piece.bufferIndex,
                            newStart,
                            piece.end,
                            newLineFeedCnt,
                            newLength
                        );

                        this.UpdateTreeMetadata(nextNode, -1, -1);
                    }
                    return true;
                }
            }

            return false;
        }

        // #endregion

        // #endregion

        // #region Tree operations
        bool Iterate(TreeNode node, Func<TreeNode, bool> callback) {
            if (node == TreeNode.SENTINEL) {
                return callback(TreeNode.SENTINEL);
            }

            var leftRet = this.Iterate(node.left, callback);
            if (!leftRet) {
                return leftRet;
            }

            return callback(node) && this.Iterate(node.right, callback);
        }

        protected string GetNodeContent(TreeNode node) {
            if (node == TreeNode.SENTINEL) {
                return "";
            }
            var buffer = this._buffers[node.piece.bufferIndex];
            string currentContent;
            var piece = node.piece;
            var startOffset = this.OffsetInBuffer(piece.bufferIndex, piece.start);
            var endOffset = this.OffsetInBuffer(piece.bufferIndex, piece.end);
            currentContent = buffer.buffer.Substring(startOffset, endOffset - startOffset);
            return currentContent;
        }

        private string GetPieceContent(Piece piece) {
            var buffer = this._buffers[piece.bufferIndex];
            var startOffset = this.OffsetInBuffer(piece.bufferIndex, piece.start);
            var endOffset = this.OffsetInBuffer(piece.bufferIndex, piece.end);
            var currentContent = buffer.buffer.Substring(startOffset, endOffset - startOffset);
            return currentContent;
        }

        /**
        *      node              node
        *     /  \              /  \
        *    a   b    <----   a    b
        *                         /
        *                        z
        */
        private TreeNode RbInsertRight(TreeNode node, Piece p) {
            var z = new TreeNode(p, NodeColor.Red);
            z.left = TreeNode.SENTINEL;
            z.right = TreeNode.SENTINEL;
            z.parent = TreeNode.SENTINEL;
            z.size_left = 0;
            z.lf_left = 0;

            var x = this.root;
            if (x == TreeNode.SENTINEL) {
                this.root = z;
                z.color = NodeColor.Black;
            } else if (node!.right == TreeNode.SENTINEL) {
                node!.right = z;
                z.parent = node!;
            } else {
                var nextNode = node!.right.Leftest();
                nextNode.left = z;
                z.parent = nextNode;
            }

            this.FixInsert(z);
            return z;
        }

        /**
        *      node              node
        *     /  \              /  \
        *    a   b     ---->   a    b
        *                       \
        *                        z
        */
        private TreeNode RbInsertLeft(TreeNode node, Piece p) {
            var z = new TreeNode(p, NodeColor.Red);
            z.left = TreeNode.SENTINEL;
            z.right = TreeNode.SENTINEL;
            z.parent = TreeNode.SENTINEL;
            z.size_left = 0;
            z.lf_left = 0;

            if (this.root == TreeNode.SENTINEL) {
                this.root = z;
                z.color = NodeColor.Black;
            } else if (node!.left == TreeNode.SENTINEL) {
                node!.left = z;
                z.parent = node!;
            } else {
                var prevNode = node!.left.Rightest(); // a
                prevNode.right = z;
                z.parent = prevNode;
            }

            this.FixInsert(z);
            return z;
        }

        private string GetContentOfSubTree(TreeNode node) {
            var str = "";

            this.Iterate(node, node => {
                str += this.GetNodeContent(node);
                return true;
            });

            return str;
        }
        // #endregion
    }

    internal struct NodePosition
    {
        /**
        * Piece Index
        */
        internal readonly TreeNode node;
        /**
        * remainer in current piece.
        */
        internal readonly int remainder;
        /**
        * node start offset in document.
        */
        internal readonly int nodeStartOffset;

        internal NodePosition(TreeNode node, int remainder, int nodeStartOffset)
        {
            this.node = node;
            this.remainder = remainder;
            this.nodeStartOffset = nodeStartOffset;
        }
    }

    internal class Piece
    {
        internal readonly int bufferIndex;
        internal readonly TextPosition start;
        internal readonly TextPosition end;
        internal readonly int length;
        internal readonly int lineFeedCnt;

        internal Piece(int bufferIndex, TextPosition start, TextPosition end, int lineFeedCnt, int length)
        {
            this.bufferIndex = bufferIndex;
            this.start = start;
            this.end = end;
            this.lineFeedCnt = lineFeedCnt;
            this.length = length;
        }
    }

    internal class StringBuffer
    {
        internal string buffer;
        internal int[] lineStarts;

        internal StringBuffer(string buffer, int[] lineStarts)
        {
            this.buffer = buffer;
            this.lineStarts = lineStarts;
        }
    }

    internal class CacheEntry
    {
        internal TreeNode node;
        internal int nodeStartOffset;
        internal int? nodeStartLineNumber;

        internal CacheEntry(TreeNode node, int nodeStartOffset, int? nodeStartLineNumber)
        {
            this.node = node;
            this.nodeStartOffset = nodeStartOffset;
            this.nodeStartLineNumber = nodeStartLineNumber;
        }
    }

    internal class PieceTreeSearchCache
    {
        private readonly int _limit;
        private List<CacheEntry> _cache;

        internal PieceTreeSearchCache(int limit)
        {
            this._limit = limit;
            this._cache = new List<CacheEntry>();
        }

        internal CacheEntry Get(int offset)
        {
            for (var i = this._cache.Count - 1; i >= 0; i--)
            {
                var nodePos = this._cache[i];
                if (nodePos.nodeStartOffset <= offset && nodePos.nodeStartOffset + nodePos.node.piece.length >= offset)
                {
                    return nodePos;
                }
            }
            return null;
        }

        internal CacheEntry Get2(int lineNumber)
        {
            for (var i = this._cache.Count - 1; i >= 0; i--)
            {
                var nodePos = this._cache[i];
                if (nodePos.nodeStartLineNumber != null && nodePos.nodeStartLineNumber < lineNumber && nodePos.nodeStartLineNumber + nodePos.node.piece.lineFeedCnt >= lineNumber)
                {
                    return nodePos;
                }
            }
            return null;
        }

        internal void Set(NodePosition nodePosition)
        {
            if (this._cache.Count >= this._limit)
            {
                this._cache.RemoveAt(0);
            }
            this._cache.Add(new CacheEntry(
                nodePosition.node,
                nodePosition.nodeStartOffset,
                null
            ));
        }

        internal void Set(CacheEntry nodePosition)
        {
            if (this._cache.Count >= this._limit)
            {
                this._cache.RemoveAt(0);
            }
            this._cache.Add(nodePosition);
        }

        internal void Validate(int offset)
        {
            var hasInvalidVal = false;
            var tmp = this._cache;
            for (var i = 0; i < tmp.Count; i++)
            {
                var nodePos = tmp[i]!;
                if (nodePos.node.parent == null || nodePos.nodeStartOffset >= offset)
                {
                    tmp[i] = null;
                    hasInvalidVal = true;
                    continue;
                }
            }

            if (hasInvalidVal)
            {
                var newArr = new List<CacheEntry>();
                foreach (var entry in tmp)
                {
                    if (entry != null)
                    {
                        newArr.Add(entry);
                    }
                }

                this._cache = newArr;
            }
        }
    }

}
