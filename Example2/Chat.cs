using System;
using System.Threading;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Example2
{
    public class Chat : WebSocketBehavior
    {
        private string _name;
        private static int _number = 0;
        private string _prefix;

        public Chat()
        {
            _prefix = "anon#";
        }

        public string Prefix
        {
            get
            {
                return _prefix;
            }

            set
            {
                _prefix = !value.IsNullOrEmpty() ? value : "anon#";
            }
        }

        private string getName()
        {
            string name = QueryString["name"];

            return !name.IsNullOrEmpty() ? name : _prefix + getNumber();
        }

        private static int getNumber()
        {
            return Interlocked.Increment(ref _number);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            if (_name == null)
                return;

            string fmt = "{0} got logged off...";
            string msg = String.Format(fmt, _name);

            Sessions.Broadcast(msg);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            string fmt = "{0}: {1}";
            string msg = String.Format(fmt, _name, e.Data);

            Sessions.Broadcast(msg);
        }

        protected override void OnOpen()
        {
            _name = getName();

            string fmt = "{0} has logged in!";
            string msg = String.Format(fmt, _name);

            Sessions.Broadcast(msg);
        }
    }
}
