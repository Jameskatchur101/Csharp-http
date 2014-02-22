﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EvHttpSharp.Interop;

namespace EvHttpSharp
{
	public class EventHttpListener : IDisposable
	{
		private readonly RequestCallback _cb;
		public delegate void RequestCallback(EventHttpRequest req);

		private EventBase _eventBase;
		private EvHttp _evHttp;
		private Thread _thread;
		private GCHandle _httpCallbackHandle;
		private EvUserEvent _syncCbUserEvent;
		private readonly Queue<Action> _syncCallbacks = new Queue<Action>();
		private bool _stop;
		private int _pendingRequests;
		private EvHttpBoundSocket _socket;

		public EventHttpListener(RequestCallback cb)
		{
			LibLocator.TryToLoadDefaultIfNotInitialized();
			_cb = cb;
		}

		public void Start(string host, ushort port)
		{
			_eventBase = Event.EventBaseNew();
			if (_eventBase.IsInvalid)
				throw new IOException("Unable to create event_base");
			_evHttp = Event.EvHttpNew(_eventBase);
			if (_evHttp.IsInvalid)
			{
				Dispose();
				throw new IOException ("Unable to create evhttp");
			}
			_socket = Event.EvHttpBindSocketWithHandle(_evHttp, host, port);
			if (_socket.IsInvalid)
			{
				Dispose();
				throw new IOException("Unable to bind to the specified address");
			}

			_thread = new Thread(MainCycle) {Priority = ThreadPriority.Highest};
			_thread.Start();
		}

		private void MainCycle()
		{
			var cb = new Event.D.evhttp_request_callback (RequestHandler);
			_httpCallbackHandle = GCHandle.Alloc (cb);
			Event.EvHttpSetAllowedMethods (_evHttp, EvHttpCmdType.All);
			Event.EvHttpSetGenCb (_evHttp, cb, GCHandle.ToIntPtr (_httpCallbackHandle));

			using (_syncCbUserEvent = new EvUserEvent(_eventBase))
			{
				_syncCbUserEvent.Triggered += SyncCallback;
				while (!_stop)
				{
					Event.EventBaseDispatch(_eventBase);
				}
			}
			//We've recieved loopbreak from actual Dispose, so dispose now
			DoDispose ();
			_httpCallbackHandle.Free ();
		}

		private void SyncCallback(object sender, EventArgs eventArgs)
		{
			lock (_syncCallbacks)
				while (_syncCallbacks.Count != 0)
					_syncCallbacks.Dequeue()();
		}

		private void RequestHandler(IntPtr request, IntPtr arg)
		{
			var req = new EventHttpRequest (this, request);
			_cb (req);
		}

		internal void DecreaseRequestCounter()
		{
			Interlocked.Decrement(ref _pendingRequests);
		}

		internal void IncreaseRequestCounter()
		{
			Interlocked.Increment(ref _pendingRequests);
		}

		internal void Sync(Action cb)
		{
			lock (_syncCallbacks)
				_syncCallbacks.Enqueue(cb);
			Event.EventActive(_syncCbUserEvent);
		}

		private void DoDispose()
		{
			if (_evHttp != null && !_evHttp.IsInvalid)
				_evHttp.Dispose ();
			if (_eventBase != null && !_eventBase.IsInvalid)
				_eventBase.Dispose();
			_pendingRequests = 0;
		}

		public void Dispose()
		{
			if (_thread == null)
				DoDispose();
			else if (_eventBase != null && !_eventBase.IsClosed)
			{
				_stop = true;
				Sync(() => Event.EventBaseLoopbreak(_eventBase));
				if (_thread != Thread.CurrentThread)
					_thread.Join ();
			}

		}

		Task SyncTask(Action cb)
		{
			var tcs = new TaskCompletionSource<int> ();
			Sync(() =>
			{
				try
				{
					cb();
					tcs.SetResult(0);
				}
				catch (Exception e)
				{
					tcs.SetException(e);
				}
			});
			return tcs.Task;
		}

		public Task StopListeningAsync()
		{
			return SyncTask(() =>
			{
				if (_socket == null || _socket.IsInvalid)
					throw new InvalidOperationException("Server isn't listening");
				Event.EvHttpDelAcceptSocket(_evHttp, _socket);
				_socket = null;
			});
		}

		public Task WaitForPendingConnections()
		{
			var tcs = new TaskCompletionSource<int>();
			Timer timer = null;
			timer = new Timer(_ =>
			{
				if (Thread.VolatileRead(ref _pendingRequests) == 0)
				{
					tcs.SetResult(0);
					// ReSharper disable once PossibleNullReferenceException
					// ReSharper disable once AccessToModifiedClosure
					timer.Dispose();
				}
			}, null, 0, 100);
			
			return tcs.Task;
		}

		public Task Shutdown()
		{
			return StopListeningAsync().ContinueWith(_ => WaitForPendingConnections()).Unwrap();
		}
	}
}
