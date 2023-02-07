#region License
/*
 * ResponseStream.cs
 *
 * This code is derived from ResponseStream.cs (System.Net) of Mono
 * (http://www.mono-project.com).
 *
 * The MIT License
 *
 * Copyright (c) 2005 Novell, Inc. (http://www.novell.com)
 * Copyright (c) 2012-2020 sta.blockhead
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

#region Authors
/*
 * Authors:
 * - Gonzalo Paniagua Javier <gonzalo@novell.com>
 */
#endregion

using System;
using System.IO;
using System.Text;

namespace WebSocketSharp.Net
{
    internal class ResponseStream : Stream
    {
        #region Private Fields

        private MemoryStream _bodyBuffer;
        private static readonly byte[] _crlf;
        private bool _disposed;
        private Stream _innerStream;
        private static readonly byte[] _lastChunk;
        private static readonly int _maxHeadersLength;
        private HttpListenerResponse _response;
        private bool _sendChunked;
        private readonly Action<byte[], int, int> _write;
        private Action<byte[], int, int> _writeBody;
        private readonly Action<byte[], int, int> _writeChunked;

        #endregion

        #region Static Constructor

        static ResponseStream()
        {
            _crlf = "\r\n"u8.ToArray(); // "\r\n"
            _lastChunk = "0\r\n\r\n"u8.ToArray(); // "0\r\n\r\n"
            _maxHeadersLength = 32768;
        }

        #endregion

        #region Internal Constructors

        internal ResponseStream(
          Stream innerStream,
          HttpListenerResponse response,
          bool ignoreWriteExceptions
        )
        {
            _innerStream = innerStream;
            _response = response;

            if (ignoreWriteExceptions)
            {
                _write = WriteWithoutThrowingException;
                _writeChunked = WriteChunkedWithoutThrowingException;
            }
            else
            {
                _write = innerStream.Write;
                _writeChunked = WriteChunked;
            }

            _bodyBuffer = new MemoryStream();
        }

        #endregion

        #region Public Properties

        public override bool CanRead
        {
            get
            {
                return false;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return !_disposed;
            }
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }

            set
            {
                throw new NotSupportedException();
            }
        }

        #endregion

        #region Private Methods

        private bool Flush(bool closing)
        {
            if (!_response.HeadersSent)
            {
                if (!FlushHeaders())
                    return false;

                _response.HeadersSent = true;

                _sendChunked = _response.SendChunked;
                _writeBody = _sendChunked ? _writeChunked : _write;
            }

            FlushBody(closing);

            return true;
        }

        private void FlushBody(bool closing)
        {
            using (_bodyBuffer)
            {
                long len = _bodyBuffer.Length;

                if (len > Int32.MaxValue)
                {
                    _bodyBuffer.Position = 0;

                    int buffLen = 1024;
                    byte[] buff = new byte[buffLen];
                    while (true)
                    {
                        int nread = _bodyBuffer.Read(buff, 0, buffLen);

                        if (nread <= 0)
                            break;

                        _writeBody(buff, 0, nread);
                    }
                }
                else if (len > 0)
                {
                    _writeBody(_bodyBuffer.GetBuffer(), 0, (int)len);
                }
            }

            if (!closing)
            {
                _bodyBuffer = new MemoryStream();
                return;
            }

            if (_sendChunked)
                _write(_lastChunk, 0, 5);

            _bodyBuffer = null;
        }

        private bool FlushHeaders()
        {
            if (!_response.SendChunked)
            {
                if (_response.ContentLength64 != _bodyBuffer.Length)
                    return false;
            }

            string statusLine = _response.StatusLine;
            WebHeaderCollection headers = _response.FullHeaders;

            MemoryStream buff = new();
            Encoding enc = Encoding.UTF8;

            using (StreamWriter writer = new(buff, enc, 256))
            {
                writer.Write(statusLine);
                writer.Write(headers.ToStringMultiValue(true));
                writer.Flush();

                int start = enc.GetPreamble().Length;
                long len = buff.Length - start;

                if (len > _maxHeadersLength)
                    return false;

                _write(buff.GetBuffer(), start, (int)len);
            }

            _response.CloseConnection = headers["Connection"] == "close";

            return true;
        }

        private static byte[] GetChunkSizeBytes(int size)
        {
            string chunkSize = String.Format("{0:x}\r\n", size);

            return Encoding.ASCII.GetBytes(chunkSize);
        }

        private void WriteChunked(byte[] buffer, int offset, int count)
        {
            byte[] size = GetChunkSizeBytes(count);

            _innerStream.Write(size, 0, size.Length);
            _innerStream.Write(buffer, offset, count);
            _innerStream.Write(_crlf, 0, 2);
        }

        private void WriteChunkedWithoutThrowingException(
          byte[] buffer, int offset, int count
        )
        {
            try
            {
                WriteChunked(buffer, offset, count);
            }
            catch
            {
            }
        }

        private void WriteWithoutThrowingException(
          byte[] buffer, int offset, int count
        )
        {
            try
            {
                _innerStream.Write(buffer, offset, count);
            }
            catch
            {
            }
        }

        #endregion

        #region Internal Methods

        internal void Close(bool force)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!force)
            {
                if (Flush(true))
                {
                    _response.Close();

                    _response = null;
                    _innerStream = null;

                    return;
                }

                _response.CloseConnection = true;
            }

            if (_sendChunked)
                _write(_lastChunk, 0, 5);

            _bodyBuffer.Dispose();
            _response.Abort();

            _bodyBuffer = null;
            _response = null;
            _innerStream = null;
        }

        internal void InternalWrite(byte[] buffer, int offset, int count)
        {
            _write(buffer, offset, count);
        }

        #endregion

        #region Public Methods

        public override IAsyncResult BeginRead(
          byte[] buffer,
          int offset,
          int count,
          AsyncCallback callback,
          object state
        )
        {
            throw new NotSupportedException();
        }

        public override IAsyncResult BeginWrite(
          byte[] buffer,
          int offset,
          int count,
          AsyncCallback callback,
          object state
        )
        {
            if (_disposed)
            {
                string name = GetType().ToString();

                throw new ObjectDisposedException(name);
            }

            return _bodyBuffer.BeginWrite(buffer, offset, count, callback, state);
        }

        public override void Close()
        {
            Close(false);
        }

        protected override void Dispose(bool disposing)
        {
            Close(!disposing);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            throw new NotSupportedException();
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            if (_disposed)
            {
                string name = GetType().ToString();

                throw new ObjectDisposedException(name);
            }

            _bodyBuffer.EndWrite(asyncResult);
        }

        public override void Flush()
        {
            if (_disposed)
                return;

            bool sendChunked = _sendChunked || _response.SendChunked;

            if (!sendChunked)
                return;

            _ = Flush(false);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed)
            {
                string name = GetType().ToString();

                throw new ObjectDisposedException(name);
            }

            _bodyBuffer.Write(buffer, offset, count);
        }

        #endregion
    }
}
