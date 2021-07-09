/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

// import { CharCode } from 'vs/base/common/charCode';
// import { IDisposable } from 'vs/base/common/lifecycle';
// import * as strings from 'vs/base/common/strings';
// import { DefaultEndOfLine, ITextBuffer, ITextBufferBuilder, ITextBufferFactory } from 'vs/editor/common/model';
// import { StringBuffer, CreateLineStarts, CreateLineStartsFast } from 'vs/editor/common/model/pieceTreeTextBuffer/pieceTreeBase';
// import { PieceTreeTextBuffer } from 'vs/editor/common/model/pieceTreeTextBuffer/pieceTreeTextBuffer';

using System.Text;
using System.Text.RegularExpressions;

/**
 * The default end of line to use when instantiating models.
 */
public enum DefaultEndOfLine
{
    /**
	 * Use line feed (\n) as the end of line character.
	 */
    LF = 1,
    /**
	 * Use carriage return and line feed (\r\n) as the end of line character.
	 */
    CRLF = 2
}


namespace PieceTree
{
/*
    public class PieceTreeTextBufferFactory : ITextBufferFactory {
        private readonly StringBuilder[] _chunks;
        private readonly string _bom;
        private readonly uint _cr;
        private readonly uint _lf;
        private readonly uint _crlf;
        private readonly bool _containsRTL;
        private readonly bool _containsUnusualLineTerminators;
        private readonly bool _isBasicASCII;
        private readonly bool _normalizeEOL;

        PieceTreeTextBufferFactory(StringBuilder[] _chunks, string _bom, uint _cr, uint _lf, uint _crlf, bool _containsRTL, bool _containsUnusualLineTerminators, bool _isBasicASCII, bool _normalizeEOL)
            : this(_chunks, _bom, _cr, _lf, _crlf, _containsRTL, _containsUnusualLineTerminators, _isBasicASCII, _normalizeEOL) { }

        private string _getEOL(DefaultEndOfLine defaultEOL) {
            var totalEOLCount = this._cr + this._lf + this._crlf;
            var totalCRCount = this._cr + this._crlf;
            if (totalEOLCount == 0) {
                // This is an empty file or a file with precisely one line
                return (defaultEOL == DefaultEndOfLine.LF ? "\n" : "\r\n");
            }
            if (totalCRCount > totalEOLCount / 2) {
                // More than half of the file contains \r\n ending lines
                return "\r\n";
            }
            // At least one line more ends in \n
            return "\n";
        }

        public create(DefaultEndOfLine defaultEOL, out ITextBuffer textBuffer, out IDisposable disposable) {
            var eol = this._getEOL(defaultEOL);
            var chunks = this._chunks;

            if (this._normalizeEOL &&
                ((eol == "\r\n" && (this._cr > 0 || this._lf > 0))
                    || (eol == "\n" && (this._cr > 0 || this._crlf > 0)))
            ) {
                // Normalize pieces
                for (int i = 0, len = chunks.Length; i < len; i++) {
                    var str = chunks[i].Replace(@"\r\n|\r|\n", eol);
                    var newLineStart = CreateLineStartsFast(str);
                    chunks[i] = new StringBuffer(str, newLineStart);
                }
            }

            const textBuffer = new PieceTreeTextBuffer(chunks, this._bom, eol, this._containsRTL, this._containsUnusualLineTerminators, this._isBasicASCII, this._normalizeEOL);
            return { textBuffer textBuffer, textBuffer disposable };
        }

        public string getFirstLineText(long lengthLimit) {
            return this._chunks[0].buffer.substr(0, lengthLimit).split(@"\r\n|\r|\n")[0];
        }
    }

    public class PieceTreeTextBufferBuilder : ITextBufferBuilder {
        private readonly StringBuilder chunks;
        private string BOM;

        private bool _hasPreviousChar;
        private number _previousChar;
        private readonly uint[] _tmpLineStarts;

        private number cr;
        private number lf;
        private number crlf;
        private bool containsRTL;
        private bool containsUnusualLineTerminators;
        private bool isBasicASCII;

        constructor() {
            this.chunks = new StringBuilder();
            this.BOM = "";

            this._hasPreviousChar = false;
            this._previousChar = 0;
            this._tmpLineStarts = new uint[];

            this.cr = 0;
            this.lf = 0;
            this.crlf = 0;
            this.containsRTL = false;
            this.containsUnusualLineTerminators = false;
            this.isBasicASCII = true;
        }

        public void acceptChunk(string chunk) {
            if (chunk.length == 0) {
                return;
            }

            if (this.chunks.length == 0) {
                if (strings.startsWithUTF8BOM(chunk)) {
                    this.BOM = strings.UTF8_BOM_CHARACTER;
                    chunk = chunk.substr(1);
                }
            }

            var lastChar = chunk.charCodeAt(chunk.length - 1);
            if (lastChar == CharCode.CarriageReturn || (lastChar >= 0xD800 && lastChar <= 0xDBFF)) {
                // last character is \r or a high surrogate => keep it back
                this._acceptChunk1(chunk.substr(0, chunk.length - 1), false);
                this._hasPreviousChar = true;
                this._previousChar = lastChar;
            } else {
                this._acceptChunk1(chunk, false);
                this._hasPreviousChar = false;
                this._previousChar = lastChar;
            }
        }

        private void _acceptChunk1(string chunk, bool allowEmptyStrings) {
            if (!allowEmptyStrings && chunk.length == 0) {
                // Nothing to do
                return;
            }

            if (this._hasPreviousChar) {
                this._acceptChunk2(String.fromCharCode(this._previousChar) + chunk);
            } else {
                this._acceptChunk2(chunk);
            }
        }

        private void _acceptChunk2(string chunk) {
            var lineStarts = CreateLineStarts(this._tmpLineStarts, chunk);

            this.chunks.push(new StringBuffer(chunk, lineStarts.lineStarts));
            this.cr += lineStarts.cr;
            this.lf += lineStarts.lf;
            this.crlf += lineStarts.crlf;

            if (this.isBasicASCII) {
                this.isBasicASCII = lineStarts.isBasicASCII;
            }
            if (!this.isBasicASCII && !this.containsRTL) {
                // No need to check if it is basic ASCII
                this.containsRTL = strings.containsRTL(chunk);
            }
            if (!this.isBasicASCII && !this.containsUnusualLineTerminators) {
                // No need to check if it is basic ASCII
                this.containsUnusualLineTerminators = strings.containsUnusualLineTerminators(chunk);
            }
        }

        public PieceTreeTextBufferFactory finish(bool normalizeEOL = true) {
            this._finish();
            return new PieceTreeTextBufferFactory(
                this.chunks,
                this.BOM,
                this.cr,
                this.lf,
                this.crlf,
                this.containsRTL,
                this.containsUnusualLineTerminators,
                this.isBasicASCII,
                normalizeEOL
            );
        }

        private void _finish() {
            if (this.chunks.length == 0) {
                this._acceptChunk1("", true);
            }

            if (this._hasPreviousChar) {
                this._hasPreviousChar = false;
                // recreate last chunk
                var lastChunk = this.chunks[this.chunks.length - 1];
                lastChunk.buffer += String.fromCharCode(this._previousChar);
                var newLineStarts = CreateLineStartsFast(lastChunk.buffer);
                lastChunk.lineStarts = newLineStarts;
                if (this._previousChar == CharCode.CarriageReturn) {
                    this.cr++;
                }
            }
        }
    }
*/
}
