
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Starflight.AI
{
	// runs work queued by background threads (kernel functions, the claude process) on the Unity main thread
	public class MainThreadDispatcher : MonoBehaviour
	{
		// the most actions run in one frame, so a flood of work cannot stall a frame
		public const int c_maxActionsPerFrame = 256;

		// the static instance of this component
		public static MainThreadDispatcher m_instance;

		// the queue is static, so work enqueued before this component wakes up (or between scenes) is kept
		static ConcurrentQueue<Action> s_queue = new ConcurrentQueue<Action>();

		// clear the queue when play mode starts, in case domain reload is switched off
		[RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.SubsystemRegistration )]
		static void ResetStatics()
		{
			// drop whatever the previous play session left behind
			s_queue = new ConcurrentQueue<Action>();

			// forget the instance of the previous play session
			m_instance = null;
		}

		// queue an action to run on the main thread (safe to call from any thread)
		public static void Enqueue( Action action )
		{
			// ignore empty actions
			if ( action == null )
			{
				return;
			}

			// add it to the queue
			s_queue.Enqueue( action );
		}

		// the number of actions waiting to run
		public static int PendingCount
		{
			get { return s_queue.Count; }
		}

		// unity awake
		void Awake()
		{
			// keep only one dispatcher, a second one would drain the same queue
			if ( ( m_instance != null ) && ( m_instance != this ) )
			{
				Debug.LogWarning( "MainThreadDispatcher: a dispatcher already exists, removing the one on " + name + "." );

				Destroy( this );

				return;
			}

			// remember the static instance
			m_instance = this;
		}

		// unity on destroy
		void OnDestroy()
		{
			// forget the static instance if it is this one
			if ( m_instance == this )
			{
				m_instance = null;
			}
		}

		// unity update
		void Update()
		{
			// run at most what was queued when this frame began, plus the cap, so actions that enqueue more cannot loop forever
			var actionsToRun = Math.Min( s_queue.Count, c_maxActionsPerFrame );

			for ( var i = 0; i < actionsToRun; i++ )
			{
				// stop if another thread emptied the queue
				if ( !s_queue.TryDequeue( out var action ) )
				{
					break;
				}

				// one action that throws must not stop the others
				try
				{
					action();
				}
				catch ( Exception exception )
				{
					Debug.LogException( exception );
				}
			}
		}
	}
}
