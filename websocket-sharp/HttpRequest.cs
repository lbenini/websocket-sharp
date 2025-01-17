#region License
/*
 * HttpRequest.cs
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

#region Contributors
/*
 * Contributors:
 * - David Burhans
 */
#endregion

using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using WebSocketSharp.Net;

namespace WebSocketSharp
{
    internal class HttpRequest : HttpBase
    {
        #region Private Fields

        private CookieCollection _cookies;
        private readonly string _method;
        private readonly string _target;

        #endregion

        #region Private Constructors

        private HttpRequest(
          string method,
          string target,
          Version version,
          NameValueCollection headers
        )
          : base(version, headers)
        {
            _method = method;
            _target = target;
        }

        #endregion

        #region Internal Constructors

        internal HttpRequest(string method, string target)
          : this(method, target, HttpVersion.Version11, new NameValueCollection())
        {
            Headers["User-Agent"] = "websocket-sharp/1.0";
        }

        #endregion

        #region Internal Properties

        internal string RequestLine
        {
            get
            {
                return String.Format(
                         "{0} {1} HTTP/{2}{3}", _method, _target, ProtocolVersion, CrLf
                       );
            }
        }

        #endregion

        #region Public Properties

        public AuthenticationResponse AuthenticationResponse
        {
            get
            {
                string val = Headers["Authorization"];

                return val != null && val.Length > 0
                       ? AuthenticationResponse.Parse(val)
                       : null;
            }
        }

        public CookieCollection Cookies
        {
            get
            {
                if (_cookies == null)
                    _cookies = Headers.GetCookies(false);

                return _cookies;
            }
        }

        public string HttpMethod
        {
            get
            {
                return _method;
            }
        }

        public bool IsWebSocketRequest
        {
            get
            {
                return _method == "GET"
                       && ProtocolVersion > HttpVersion.Version10
                       && Headers.Upgrades("websocket");
            }
        }

        public override string MessageHeader
        {
            get
            {
                return RequestLine + HeaderSection;
            }
        }

        public string RequestTarget
        {
            get
            {
                return _target;
            }
        }

        #endregion

        #region Internal Methods

        internal static HttpRequest CreateConnectRequest(Uri targetUri)
        {
            string host = targetUri.DnsSafeHost;
            int port = targetUri.Port;
            string authority = String.Format("{0}:{1}", host, port);

            HttpRequest ret = new("CONNECT", authority);

            ret.Headers["Host"] = port != 80 ? authority : host;

            return ret;
        }

        internal static HttpRequest CreateWebSocketHandshakeRequest(Uri targetUri)
        {
            HttpRequest ret = new("GET", targetUri.PathAndQuery);

            NameValueCollection headers = ret.Headers;

            int port = targetUri.Port;
            string schm = targetUri.Scheme;
            bool defaultPort = (port == 80 && schm == "ws")
                        || (port == 443 && schm == "wss");

            headers["Host"] = !defaultPort
                              ? targetUri.Authority
                              : targetUri.DnsSafeHost;

            headers["Upgrade"] = "websocket";
            headers["Connection"] = "Upgrade";

            return ret;
        }

        internal HttpResponse GetResponse(Stream stream, int millisecondsTimeout)
        {
            WriteTo(stream);

            return HttpResponse.ReadResponse(stream, millisecondsTimeout);
        }

        internal static HttpRequest Parse(string[] messageHeader)
        {
            int len = messageHeader.Length;

            if (len == 0)
            {
                string msg = "An empty request header.";

                throw new ArgumentException(msg);
            }

            string[] rlParts = messageHeader[0].Split(new[] { ' ' }, 3);

            if (rlParts.Length != 3)
            {
                string msg = "It includes an invalid request line.";

                throw new ArgumentException(msg);
            }

            string method = rlParts[0];
            string target = rlParts[1];
            Version ver = rlParts[2].Substring(5).ToVersion();

            WebHeaderCollection headers = new();

            for (int i = 1; i < len; i++)
                headers.InternalSet(messageHeader[i], false);

            return new HttpRequest(method, target, ver, headers);
        }

        internal static HttpRequest ReadRequest(
          Stream stream, int millisecondsTimeout
        )
        {
            return Read<HttpRequest>(stream, Parse, millisecondsTimeout);
        }

        #endregion

        #region Public Methods

        public void SetCookies(CookieCollection cookies)
        {
            if (cookies == null || cookies.Count == 0)
                return;

            StringBuilder buff = new(64);

            foreach (Cookie cookie in cookies.Sorted)
            {
                if (cookie.Expired)
                    continue;

                _ = buff.AppendFormat("{0}; ", cookie);
            }

            int len = buff.Length;

            if (len <= 2)
                return;

            buff.Length = len - 2;

            Headers["Cookie"] = buff.ToString();
        }

        #endregion
    }
}
