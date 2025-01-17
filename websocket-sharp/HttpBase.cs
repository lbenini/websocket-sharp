#region License
/*
 * HttpBase.cs
 *
 * The MIT License
 *
 * Copyright (c) 2012-2022 sta.blockhead
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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using WebSocketSharp.Net;

namespace WebSocketSharp
{
    internal abstract class HttpBase
    {
        #region Private Fields

        private readonly NameValueCollection _headers;
        private static readonly int _maxMessageHeaderLength;
        private string _messageBody;
        private byte[] _messageBodyData;
        private readonly Version _version;

        #endregion

        #region Protected Fields

        protected static readonly string CrLf;
        protected static readonly string CrLfHt;
        protected static readonly string CrLfSp;

        #endregion

        #region Static Constructor

        static HttpBase()
        {
            _maxMessageHeaderLength = 8192;

            CrLf = "\r\n";
            CrLfHt = "\r\n\t";
            CrLfSp = "\r\n ";
        }

        #endregion

        #region Protected Constructors

        protected HttpBase(Version version, NameValueCollection headers)
        {
            _version = version;
            _headers = headers;
        }

        #endregion

        #region Internal Properties

        internal byte[] MessageBodyData
        {
            get
            {
                return _messageBodyData;
            }
        }

        #endregion

        #region Protected Properties

        protected string HeaderSection
        {
            get
            {
                StringBuilder buff = new(64);

                foreach (string key in _headers.AllKeys)
                    _ = buff.AppendFormat("{0}: {1}{2}", key, _headers[key], CrLf);

                _ = buff.Append(CrLf);

                return buff.ToString();
            }
        }

        #endregion

        #region Public Properties

        public bool HasMessageBody
        {
            get
            {
                return _messageBodyData != null;
            }
        }

        public NameValueCollection Headers
        {
            get
            {
                return _headers;
            }
        }

        public string MessageBody
        {
            get
            {
                if (_messageBody == null)
                    _messageBody = getMessageBody();

                return _messageBody;
            }
        }

        public abstract string MessageHeader { get; }

        public Version ProtocolVersion
        {
            get
            {
                return _version;
            }
        }

        #endregion

        #region Private Methods

        private string getMessageBody()
        {
            if (_messageBodyData == null || _messageBodyData.LongLength == 0)
                return String.Empty;

            string contentType = _headers["Content-Type"];

            Encoding enc = contentType != null && contentType.Length > 0
                ? HttpUtility.GetEncoding(contentType)
                : Encoding.UTF8;

            return enc.GetString(_messageBodyData);
        }

        private static byte[] readMessageBodyFrom(Stream stream, string length)
        {

            if (!Int64.TryParse(length, out long len))
            {
                string msg = "It cannot be parsed.";

                throw new ArgumentException(msg, "length");
            }

            if (len < 0)
            {
                string msg = "It is less than zero.";

                throw new ArgumentOutOfRangeException("length", msg);
            }

            return len > 1024
                   ? stream.ReadBytes(len, 1024)
                   : len > 0
                     ? stream.ReadBytes((int)len)
                     : null;
        }

        private static string[] readMessageHeaderFrom(Stream stream)
        {
            List<byte> buff = new();
            int cnt = 0;
            Action<int> add =
              i =>
              {
                  if (i == -1)
                  {
                      string msg = "The header could not be read from the data stream.";

                      throw new EndOfStreamException(msg);
                  }

                  buff.Add((byte)i);

                  cnt++;
              };

            bool end = false;

            do
            {
                end = stream.ReadByte().IsEqualTo('\r', add)
                      && stream.ReadByte().IsEqualTo('\n', add)
                      && stream.ReadByte().IsEqualTo('\r', add)
                      && stream.ReadByte().IsEqualTo('\n', add);

                if (cnt > _maxMessageHeaderLength)
                {
                    string msg = "The length of the header is greater than the max length.";

                    throw new InvalidOperationException(msg);
                }
            }
            while (!end);

            byte[] bytes = buff.ToArray();

            return Encoding.UTF8.GetString(bytes)
                   .Replace(CrLfSp, " ")
                   .Replace(CrLfHt, " ")
                   .Split(new[] { CrLf }, StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion

        #region Internal Methods

        internal void WriteTo(Stream stream)
        {
            byte[] bytes = ToByteArray();

            stream.Write(bytes, 0, bytes.Length);
        }

        #endregion

        #region Protected Methods

        protected static T Read<T>(
          Stream stream, Func<string[], T> parser, int millisecondsTimeout
        )
          where T : HttpBase
        {
            T ret = null;

            bool timeout = false;
            Timer timer = new(
                    state =>
                    {
                        timeout = true;
                        stream.Close();
                    },
                    null,
                    millisecondsTimeout,
                    -1
                  );

            Exception exception = null;

            try
            {
                string[] header = readMessageHeaderFrom(stream);
                ret = parser(header);

                string contentLen = ret.Headers["Content-Length"];

                if (contentLen != null && contentLen.Length > 0)
                    ret._messageBodyData = readMessageBodyFrom(stream, contentLen);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                _ = timer.Change(-1, -1);
                timer.Dispose();
            }

            if (timeout)
            {
                string msg = "A timeout has occurred.";

                throw new WebSocketException(msg);
            }

            if (exception != null)
            {
                string msg = "An exception has occurred.";

                throw new WebSocketException(msg, exception);
            }

            return ret;
        }

        #endregion

        #region Public Methods

        public byte[] ToByteArray()
        {
            byte[] headerData = Encoding.UTF8.GetBytes(MessageHeader);

            return _messageBodyData != null
                   ? headerData.Concat(_messageBodyData).ToArray()
                   : headerData;
        }

        public override string ToString()
        {
            return _messageBodyData != null
                   ? MessageHeader + MessageBody
                   : MessageHeader;
        }

        #endregion
    }
}
