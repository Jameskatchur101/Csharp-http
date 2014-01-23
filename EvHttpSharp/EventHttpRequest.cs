﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using EvHttpSharp.Interop;

namespace EvHttpSharp
{
	public class EventHttpRequest
	{
		private readonly EventHttpListener _listener;
		private readonly EvHttpRequest _handle;
		public string Method { get; set; }
		public string Uri { get; set; }
		public string Host { get; set; }
		public IDictionary<string, IEnumerable<string>> Headers { get; set; }
		public string UserHostAddress { get; set; }
		public byte[] RequestBody { get; set; }

		public EventHttpRequest(EventHttpListener listener, IntPtr handle)
		{
			_listener = listener;
			_handle = new EvHttpRequest(handle);
			Method = Event.EvHttpRequestGetCommand(_handle).ToString().ToUpper();
			Uri = Marshal.PtrToStringAnsi(Event.EvHttpRequestGetUri(_handle));
			var pHost = Event.EvHttpRequestGetHost(_handle);
			if (pHost != IntPtr.Zero)
				Host = Marshal.PtrToStringAnsi(pHost);
			Headers = EvKeyValuePair.ExtractDictinary(Event.EvHttpRequestGetInputHeaders(_handle));
			if (Headers.ContainsKey("Host"))
				Host = Headers["Host"].First().Split(':')[0];

			var evBuffer = new EvBuffer(Event.EvHttpRequestGetInputBuffer(_handle), false);
			if (!evBuffer.IsInvalid)
			{
				var len = Event.EvBufferGetLength(evBuffer).ToInt32();
				RequestBody = new byte[len];
				Event.EvBufferRemove(evBuffer, RequestBody, new IntPtr(len));
			}

			var conn = Event.EvHttpRequestGetConnection(_handle);
			IntPtr pHostString = IntPtr.Zero;
			ushort port = 0;
			Event.EvHttpConnectionGetPeer(conn, ref pHostString, ref port);
			UserHostAddress = Marshal.PtrToStringAnsi(pHostString);

		}


		public void Respond(System.Net.HttpStatusCode code, IDictionary<string, string> headers, byte[] body)
		{
			var pHeaders = Event.EvHttpRequestGetOutputHeaders(_handle);
			foreach (var header in headers.Where(h => h.Key != "Content-Length"))
				Event.EvHttpAddHeader(pHeaders, header.Key, header.Value);
			Event.EvHttpAddHeader(pHeaders, "Content-Length", body.Length.ToString());
			var buffer = Event.EvBufferNew();
			Event.EvBufferAdd(buffer, body, new IntPtr(body.Length));
			_listener.Sync(() =>
				{
					Event.EvHttpSendReply(_handle, (int) code, code.ToString(), buffer);
					buffer.Dispose();
				});
		}
	}
}