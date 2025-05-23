﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace System.Net.Mime
{
    internal sealed class Base64Encoder : ByteEncoder
    {
        private static ReadOnlySpan<byte> Base64EncodeMap => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="u8;

        //the number of bytes needed to encode three bytes
        private const int SizeOfBase64EncodedBlock = 4;

        private readonly int _lineLength;
        private readonly Base64WriteStateInfo _writeState;

        internal override WriteStateInfoBase WriteState
        {
            get
            {
                Debug.Assert(_writeState != null, "_writeState was null");
                return _writeState;
            }
        }

        internal Base64Encoder(Base64WriteStateInfo writeStateInfo, int lineLength)
        {
            _writeState = writeStateInfo;
            _lineLength = lineLength;
        }
        protected override bool HasSpecialEncodingForCRLF => false;

        // no special encoding of CRLF in Base64. this method will not be used
        protected override void AppendEncodedCRLF()
        {
            throw new InvalidOperationException();
        }

        protected override bool LineBreakNeeded(byte b)
        {
            return LineBreakNeeded(1);
        }

        protected override bool LineBreakNeeded(ReadOnlySpan<byte> bytes)
        {
            return LineBreakNeeded(bytes.Length);
        }

        private bool LineBreakNeeded(int numberOfBytesToAppend)
        {
            if (_lineLength == -1)
            {
                return false;
            }

            int bytesLeftInCurrentBlock;
            int numberOfCharsLeftInCurrentBlock;
            switch (_writeState.Padding)
            {
                case 2: // 1 byte was encoded from 3
                    bytesLeftInCurrentBlock = 2;
                    numberOfCharsLeftInCurrentBlock = 3;
                    break;
                case 1: // 2 bytes were encoded from 3
                    bytesLeftInCurrentBlock = 1;
                    numberOfCharsLeftInCurrentBlock = 2;
                    break;
                case 0: // all 3 bytes were encoded
                    bytesLeftInCurrentBlock = 0;
                    numberOfCharsLeftInCurrentBlock = 0;
                    break;
                default:
                    Debug.Fail("paddind was not in range [0,2]");
                    bytesLeftInCurrentBlock = 0;
                    numberOfCharsLeftInCurrentBlock = 0;
                    break;
            }

            int numberOfBytesInNewBlock = numberOfBytesToAppend - bytesLeftInCurrentBlock;
            if (numberOfBytesInNewBlock <= 0)
            {
                return false;
            }

            int numberOfBlocksToAppend = numberOfBytesInNewBlock / 3 + (numberOfBytesInNewBlock % 3 == 0 ? 0 : 1);
            int numberOfCharsToAppend = numberOfCharsLeftInCurrentBlock + numberOfBlocksToAppend * SizeOfBase64EncodedBlock;

            return WriteState.CurrentLineLength + numberOfCharsToAppend + _writeState.FooterLength > _lineLength;
        }

        protected override int GetCodepointSize(string value, int i)
        {
            return IsSurrogatePair(value, i) ? 2 : 1;
        }

        public override void AppendPadding()
        {
            switch (_writeState.Padding)
            {
                case 0: // No padding needed
                    break;
                case 2: // 2 character padding needed (1 byte was encoded instead of 3)
                    _writeState.Append(Base64EncodeMap[_writeState.LastBits]);
                    _writeState.Append(Base64EncodeMap[64]);
                    _writeState.Append(Base64EncodeMap[64]);
                    _writeState.Padding = 0;
                    break;
                case 1: // 1 character padding needed (2 bytes were encoded instead of 3)
                    _writeState.Append(Base64EncodeMap[_writeState.LastBits]);
                    _writeState.Append(Base64EncodeMap[64]);
                    _writeState.Padding = 0;
                    break;
                default:
                    Debug.Fail("paddind was not in range [0,2]");
                    break;
            }
        }

        protected override void AppendEncodedByte(byte b)
        {
            // Base64 encoding transforms a group of 3 bytes into a group of 4 Base64 characters
            switch (_writeState.Padding)
            {
                case 0: // Add first byte of 3
                    _writeState.Append(Base64EncodeMap[(b & 0xfc) >> 2]);
                    _writeState.LastBits = (byte)((b & 0x03) << 4);
                    _writeState.Padding = 2;
                    break;
                case 2: // Add second byte of 3
                    _writeState.Append(Base64EncodeMap[_writeState.LastBits | ((b & 0xf0) >> 4)]);
                    _writeState.LastBits = (byte)((b & 0x0f) << 2);
                    _writeState.Padding = 1;
                    break;
                case 1: // Add third byte of 3
                    _writeState.Append(Base64EncodeMap[_writeState.LastBits | ((b & 0xc0) >> 6)]);
                    _writeState.Append(Base64EncodeMap[(b & 0x3f)]);
                    _writeState.Padding = 0;
                    break;
                default:
                    Debug.Fail("paddind was not in range [0,2]");
                    break;
            }
        }
    }
}
