
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UnityEngine;

namespace Starflight.AI
{
	// builds a semantic kernel that talks to claude through the command line, and runs world simulations with it
	//
	// this is a development tool: it needs Claude Code installed and signed in on the machine that runs it, and each
	// request starts a claude process that takes seconds, so it is not something to call every frame
	public class MetagameOrchestrator : MonoBehaviour
	{
		// the static instance of this component
		public static MetagameOrchestrator m_instance;

		[Header( "Claude command line" )]

		[Tooltip( "The claude executable: a full path, or a name looked up on the PATH (claude.exe from the native installer, or claude.cmd from npm)." )]
		public string m_claudeExecutable = "claude";

		[Tooltip( "Passed to claude with --model (for example sonnet or haiku). Empty uses the command line's default." )]
		public string m_model = "";

		[Tooltip( "How long one call to claude may take, in seconds." )]
		public int m_timeoutSeconds = 180;

		[Tooltip( "The most rounds of tool calls before claude has to answer." )]
		public int m_maximumToolRounds = 5;

		[Header( "Simulation" )]

		[Tooltip( "The faction that starts the border dispute." )]
		public string m_factionA = "Thrynn";

		[Tooltip( "The faction the border dispute is aimed at." )]
		public string m_factionB = "Elowan";

		[Tooltip( "Run one world simulation when the scene starts." )]
		public bool m_simulateOnStart = false;

		// the kernel (null until Start)
		Kernel m_kernel;

		// the plugin that the kernel calls into
		WorldStatePlugin m_worldStatePlugin;

		// cancels a running request when this object goes away
		CancellationTokenSource m_cancellationTokenSource;

		// true while a simulation runs
		bool m_isRunning;

		// the kernel, for other scripts that want to add plugins or run their own prompts
		public Kernel KernelInstance
		{
			get { return m_kernel; }
		}

		// the world state plugin, to listen to its events
		public WorldStatePlugin WorldState
		{
			get { return m_worldStatePlugin; }
		}

		// true while a simulation runs
		public bool IsRunning
		{
			get { return m_isRunning; }
		}

		// unity awake
		void Awake()
		{
			// remember the static instance
			m_instance = this;

			// one token source for the life of this object
			m_cancellationTokenSource = new CancellationTokenSource();
		}

		// unity start
		void Start()
		{
			// the plugin enqueues its logs, so there has to be a dispatcher to run them
			if ( MainThreadDispatcher.m_instance == null )
			{
				Debug.LogWarning( "MetagameOrchestrator: no MainThreadDispatcher in the scene, adding one to " + name + "." );

				gameObject.AddComponent<MainThreadDispatcher>();
			}

			// the options are read here on the main thread (Application paths may not be read from other threads)
			var options = new ClaudeCliOptions
			{
				m_executable = m_claudeExecutable,
				m_model = m_model,
				m_workingDirectory = Path.Combine( Application.temporaryCachePath, "ClaudeCli" ),
				m_timeout = TimeSpan.FromSeconds( Mathf.Max( 10, m_timeoutSeconds ) ),
				m_maximumToolRounds = Mathf.Max( 0, m_maximumToolRounds ),
			};

			// build the kernel with the claude service and the world state plugin
			var builder = Kernel.CreateBuilder();

			builder.Services.AddSingleton<IChatCompletionService>( new ClaudeCliChatService( options ) );

			m_worldStatePlugin = new WorldStatePlugin();

			builder.Plugins.AddFromObject( m_worldStatePlugin, "WorldState" );

			m_kernel = builder.Build();

			Debug.Log( "MetagameOrchestrator: semantic kernel ready with " + m_kernel.Plugins.Count + " plugin(s)." );

			// run one simulation if asked to
			if ( m_simulateOnStart )
			{
				RunWorldSimulationFromInspector();
			}
		}

		// unity on destroy
		void OnDestroy()
		{
			// stop a running request, which also ends its claude process
			if ( m_cancellationTokenSource != null )
			{
				m_cancellationTokenSource.Cancel();
				m_cancellationTokenSource.Dispose();
				m_cancellationTokenSource = null;
			}

			// forget the static instance if it is this one
			if ( m_instance == this )
			{
				m_instance = null;
			}
		}

		// asks claude to simulate a border dispute between the two factions; claude calls generate_faction_event to decide
		// the outcome, and the text it returns is the narration (call from the main thread; the await comes back to it)
		public async Task<string> TriggerWorldSimulation()
		{
			// the kernel is built in Start
			if ( m_kernel == null )
			{
				throw new InvalidOperationException( "MetagameOrchestrator: the kernel is not ready yet (it is built in Start)." );
			}

			// one simulation at a time: each one is a claude process
			if ( m_isRunning )
			{
				throw new InvalidOperationException( "MetagameOrchestrator: a world simulation is already running." );
			}

			m_isRunning = true;

			try
			{
				var chatService = m_kernel.GetRequiredService<IChatCompletionService>();

				// the conversation for this simulation
				var chatHistory = new ChatHistory( "You are the game master of Starflight, the 1986 space exploration game. You narrate events between the alien factions of the galaxy in two or three short sentences, in the voice of an interstellar news bulletin. Outcomes are never yours to decide: you get them from the generate_faction_event tool." );

				chatHistory.AddUserMessage( "Simulate a border dispute between the " + m_factionA + " and the " + m_factionB + ". Resolve it with the tool, then narrate the result." );

				// let the service offer the kernel's functions to claude and run the ones it calls
				var settings = new PromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() };

				var reply = await chatService.GetChatMessageContentAsync( chatHistory, settings, m_kernel, m_cancellationTokenSource.Token );

				var narration = reply.Content ?? "";

				Debug.Log( "[WorldState] " + narration );

				return narration;
			}
			finally
			{
				m_isRunning = false;
			}
		}

		// runs a simulation from the component's context menu, and logs what goes wrong instead of losing it
		[ContextMenu( "Trigger World Simulation" )]
		public async void RunWorldSimulationFromInspector()
		{
			try
			{
				await TriggerWorldSimulation();
			}
			catch ( OperationCanceledException )
			{
				// the object went away while claude was working
			}
			catch ( Exception exception )
			{
				Debug.LogException( exception, this );
			}
		}
	}
}
