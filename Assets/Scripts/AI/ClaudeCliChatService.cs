
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Starflight.AI
{
	// settings for the claude command line chat service
	public sealed class ClaudeCliOptions
	{
		// the claude executable: a full path, or a name that is looked up on the PATH
		public string m_executable = "claude";

		// the model alias or id passed with --model (empty uses the command line's default)
		public string m_model = "";

		// the folder the process runs in (keep it away from the project, or claude reads the project's CLAUDE.md)
		public string m_workingDirectory = "";

		// how long one call to claude may take
		public TimeSpan m_timeout = TimeSpan.FromSeconds( 180 );

		// the most rounds of tool calls before the model has to answer
		public int m_maximumToolRounds = 5;
	}

	// a semantic kernel chat completion service that runs the claude command line (claude -p) for each request
	//
	// the prompt goes to the process on standard input and never on the command line, so nothing the model or the
	// player writes is ever parsed by cmd.exe or by the windows argument splitter; the arguments are all fixed text
	//
	// the command line has no function calling of its own that semantic kernel can see, so this service does it:
	// the functions that FunctionChoiceBehavior offers are described in the system prompt, claude answers with a
	// json object (enforced with --json-schema), and when that object asks for tool calls they are invoked here and
	// their results go back to claude in the next round, the same loop the openai connector runs
	public sealed class ClaudeCliChatService : IChatCompletionService
	{
		// the json schema claude's answer has to match
		const string c_responseSchema =
			"{\"type\":\"object\",\"properties\":{" +
			"\"type\":{\"type\":\"string\",\"enum\":[\"final\",\"tool_calls\"]}," +
			"\"text\":{\"type\":\"string\"}," +
			"\"tool_calls\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{" +
			"\"name\":{\"type\":\"string\"}," +
			"\"arguments\":{\"type\":\"object\"}}," +
			"\"required\":[\"name\",\"arguments\"]}}}," +
			"\"required\":[\"type\"]}";

		// separates the plugin name and the function name in the names claude sees
		const char c_nameSeparator = '-';

		// the settings of this service
		readonly ClaudeCliOptions m_options;

		// the attributes semantic kernel reads from a service
		readonly Dictionary<string, object?> m_attributes = new Dictionary<string, object?>();

		// counts calls, to give each system prompt file its own name
		int m_callCounter;

		// constructor
		public ClaudeCliChatService( ClaudeCliOptions options )
		{
			// keep the settings
			m_options = options ?? throw new ArgumentNullException( nameof( options ) );

			// report the model, if one was chosen
			if ( !string.IsNullOrEmpty( m_options.m_model ) )
			{
				m_attributes[ "ModelId" ] = m_options.m_model;
			}
		}

		// IAIService
		public IReadOnlyDictionary<string, object?> Attributes
		{
			get { return m_attributes; }
		}

		// IChatCompletionService
		public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync( ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default )
		{
			// a request needs a history
			if ( chatHistory == null )
			{
				throw new ArgumentNullException( nameof( chatHistory ) );
			}

			// the tool rounds are bounded, so a model that keeps calling tools cannot loop forever
			var maximumRounds = Math.Max( 0, m_options.m_maximumToolRounds );

			// the last round offers no tools, so it always ends with an answer
			for ( var round = 0; round <= maximumRounds; round++ )
			{
				// work out which functions this round offers (none once the rounds are used up)
				var configuration = ( round < maximumRounds ) ? GetFunctionConfiguration( chatHistory, executionSettings, kernel, round ) : null;

				var functions = ( configuration?.Functions != null ) && ( configuration.Choice != FunctionChoice.None ) ? configuration.Functions : null;

				var toolsAllowed = ( functions != null ) && ( functions.Count > 0 );

				var toolRequired = toolsAllowed && ( configuration!.Choice == FunctionChoice.Required ) && ( round == 0 );

				// ask claude
				var reply = await RunClaudeAsync( BuildSystemPrompt( chatHistory, toolsAllowed ? functions : null, toolRequired, round >= maximumRounds && maximumRounds > 0 ), BuildTranscript( chatHistory ), cancellationToken ).ConfigureAwait( false );

				// a final answer (or tool calls when no tools were offered) ends the request
				if ( !toolsAllowed || ( reply.m_toolCalls.Count == 0 ) )
				{
					return new List<ChatMessageContent> { new ChatMessageContent( AuthorRole.Assistant, reply.m_text, ModelIdOrNull() ) };
				}

				// turn the tool calls into function call contents
				var assistantMessage = new ChatMessageContent( AuthorRole.Assistant, string.IsNullOrEmpty( reply.m_text ) ? null : reply.m_text, ModelIdOrNull() );

				var calls = new List<FunctionCallContent>();

				for ( var i = 0; i < reply.m_toolCalls.Count; i++ )
				{
					var call = CreateFunctionCall( reply.m_toolCalls[ i ], functions!, round, i );

					assistantMessage.Items.Add( call );

					calls.Add( call );
				}

				// without auto invoke the caller runs the functions, so hand the calls back
				if ( !configuration!.AutoInvoke || ( kernel == null ) )
				{
					return new List<ChatMessageContent> { assistantMessage };
				}

				// the calls become part of the history, as the other connectors do it
				chatHistory.Add( assistantMessage );

				// run each function and add its result to the history
				foreach ( var call in calls )
				{
					FunctionResultContent result;

					try
					{
						result = await call.InvokeAsync( kernel, cancellationToken ).ConfigureAwait( false );
					}
					catch ( OperationCanceledException )
					{
						throw;
					}
					catch ( Exception exception )
					{
						// claude sees the error and can try again or answer without it
						result = new FunctionResultContent( call, "Error: " + exception.Message );
					}

					chatHistory.Add( result.ToChatMessage() );
				}
			}

			// not reached: the round after the last tool round has no tools and returns
			throw new InvalidOperationException( "The tool rounds ended without an answer." );
		}

		// IChatCompletionService (the command line answers in one piece, so this yields one chunk per message)
		public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync( ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default )
		{
			var messages = await GetChatMessageContentsAsync( chatHistory, executionSettings, kernel, cancellationToken ).ConfigureAwait( false );

			foreach ( var message in messages )
			{
				yield return new StreamingChatMessageContent( message.Role, message.Content, message.InnerContent, 0, message.ModelId );
			}
		}

		// the model id for the messages, or null when the command line's default model is used
		string? ModelIdOrNull()
		{
			return string.IsNullOrEmpty( m_options.m_model ) ? null : m_options.m_model;
		}

		// asks the function choice behavior which functions this round offers
		static FunctionChoiceBehaviorConfiguration? GetFunctionConfiguration( ChatHistory chatHistory, PromptExecutionSettings? executionSettings, Kernel? kernel, int round )
		{
			var behavior = executionSettings?.FunctionChoiceBehavior;

			if ( behavior == null )
			{
				return null;
			}

			return behavior.GetConfiguration( new FunctionChoiceBehaviorConfigurationContext( chatHistory ) { Kernel = kernel, RequestSequenceIndex = round } );
		}

		// the name claude sees for a function
		static string GetToolName( KernelFunction function )
		{
			return string.IsNullOrEmpty( function.PluginName ) ? function.Name : function.PluginName + c_nameSeparator + function.Name;
		}

		// builds a function call content from one of claude's tool calls
		static FunctionCallContent CreateFunctionCall( ToolCall toolCall, IReadOnlyList<KernelFunction> functions, int round, int index )
		{
			var callId = "call_" + round.ToString( CultureInfo.InvariantCulture ) + "_" + index.ToString( CultureInfo.InvariantCulture );

			// only a function that was offered may run
			KernelFunction? match = null;

			foreach ( var function in functions )
			{
				if ( string.Equals( GetToolName( function ), toolCall.m_name, StringComparison.Ordinal ) )
				{
					match = function;

					break;
				}
			}

			if ( match == null )
			{
				return new FunctionCallContent( toolCall.m_name, null, callId, toolCall.m_arguments ) { Exception = new KeyNotFoundException( "There is no tool named '" + toolCall.m_name + "'." ) };
			}

			return new FunctionCallContent( match.Name, match.PluginName, callId, toolCall.m_arguments );
		}

		// the system prompt: what the service expects, the tools on offer, and the system messages of the history
		static string BuildSystemPrompt( ChatHistory chatHistory, IReadOnlyList<KernelFunction>? functions, bool toolRequired, bool roundsUsedUp )
		{
			var builder = new StringBuilder();

			builder.AppendLine( "You are the language model behind a chat completion service inside a game. You have no tools of your own: do not try to read files, run commands or browse." );
			builder.AppendLine( "Your whole answer is one JSON object that matches the schema you were given." );
			builder.AppendLine( "To answer the user, set \"type\" to \"final\" and put the answer in \"text\"." );

			if ( functions != null )
			{
				builder.AppendLine( "You can call the tools listed below. To call one or more, set \"type\" to \"tool_calls\" and list them in \"tool_calls\", each with its \"name\" and its \"arguments\" as a JSON object that matches its parameter schema. You will get their results in the next turn." );
				builder.AppendLine( "Call a tool when its description says it is the way to do what the user asks; do not make up a result that a tool would give." );

				if ( toolRequired )
				{
					builder.AppendLine( "You must call at least one tool before you answer." );
				}

				builder.AppendLine();
				builder.AppendLine( "# Tools" );

				foreach ( var function in functions )
				{
					builder.AppendLine();
					builder.AppendLine( "## " + GetToolName( function ) );
					builder.AppendLine( function.Description );
					builder.AppendLine( "Parameters: " + GetParameterSchema( function ) );
				}
			}
			else if ( roundsUsedUp )
			{
				builder.AppendLine( "The tools are no longer available. Answer the user with what you have." );
			}

			// the system messages of the history follow
			foreach ( var message in chatHistory )
			{
				if ( ( message.Role == AuthorRole.System ) || ( message.Role == AuthorRole.Developer ) )
				{
					builder.AppendLine();
					builder.AppendLine( "# Instructions" );
					builder.AppendLine( message.Content );
				}
			}

			return builder.ToString();
		}

		// the json schema of a function's parameters
		static string GetParameterSchema( KernelFunction function )
		{
			var schema = function.JsonSchema;

			return ( schema.ValueKind == JsonValueKind.Undefined ) ? "{}" : schema.GetRawText();
		}

		// the conversation so far, as text for claude's standard input
		static string BuildTranscript( ChatHistory chatHistory )
		{
			var builder = new StringBuilder();

			builder.AppendLine( "The conversation so far:" );

			foreach ( var message in chatHistory )
			{
				// the system messages are in the system prompt
				if ( ( message.Role == AuthorRole.System ) || ( message.Role == AuthorRole.Developer ) )
				{
					continue;
				}

				builder.AppendLine();
				builder.AppendLine( "### " + message.Role.Label );

				foreach ( var item in message.Items )
				{
					if ( item is TextContent text )
					{
						builder.AppendLine( text.Text );
					}
					else if ( item is FunctionCallContent call )
					{
						builder.AppendLine( "[tool call " + call.Id + "] " + ( string.IsNullOrEmpty( call.PluginName ) ? call.FunctionName : call.PluginName + c_nameSeparator + call.FunctionName ) + " " + SerializeArguments( call.Arguments ) );
					}
					else if ( item is FunctionResultContent result )
					{
						builder.AppendLine( "[tool result " + result.CallId + "] " + Convert.ToString( result.Result, CultureInfo.InvariantCulture ) );
					}
				}
			}

			builder.AppendLine();
			builder.AppendLine( "### assistant" );
			builder.AppendLine( "Answer now with the JSON object." );

			return builder.ToString();
		}

		// the arguments of a tool call, as json text
		static string SerializeArguments( KernelArguments? arguments )
		{
			if ( arguments == null )
			{
				return "{}";
			}

			var values = new Dictionary<string, string?>();

			foreach ( var pair in arguments )
			{
				values[ pair.Key ] = Convert.ToString( pair.Value, CultureInfo.InvariantCulture );
			}

			return JsonSerializer.Serialize( values );
		}

		// one tool call from claude's answer
		sealed class ToolCall
		{
			public string m_name = "";
			public KernelArguments m_arguments = new KernelArguments();
		}

		// claude's answer
		sealed class Reply
		{
			public string m_text = "";
			public List<ToolCall> m_toolCalls = new List<ToolCall>();
		}

		// runs claude once and reads its answer
		async Task<Reply> RunClaudeAsync( string systemPrompt, string transcript, CancellationToken cancellationToken )
		{
			// the system prompt goes in a file, so it never has to be quoted on the command line
			var workingDirectory = string.IsNullOrEmpty( m_options.m_workingDirectory ) ? Path.GetTempPath() : m_options.m_workingDirectory;

			Directory.CreateDirectory( workingDirectory );

			var systemPromptPath = Path.Combine( workingDirectory, "claude-system-prompt-" + Process.GetCurrentProcess().Id.ToString( CultureInfo.InvariantCulture ) + "-" + Interlocked.Increment( ref m_callCounter ).ToString( CultureInfo.InvariantCulture ) + ".txt" );

			File.WriteAllText( systemPromptPath, systemPrompt, new UTF8Encoding( false ) );

			try
			{
				// fixed arguments only: the prompt goes on standard input
				var arguments = new List<string>
				{
					"-p",
					"--output-format", "json",
					"--tools", "",
					"--strict-mcp-config",
					"--no-session-persistence",
					"--disable-slash-commands",
					"--system-prompt-file", systemPromptPath,
					"--json-schema", c_responseSchema,
				};

				if ( !string.IsNullOrEmpty( m_options.m_model ) )
				{
					arguments.Add( "--model" );
					arguments.Add( m_options.m_model );
				}

				var output = await RunProcessAsync( arguments, transcript, workingDirectory, cancellationToken ).ConfigureAwait( false );

				return ParseReply( output );
			}
			finally
			{
				try
				{
					File.Delete( systemPromptPath );
				}
				catch ( IOException )
				{
				}
				catch ( UnauthorizedAccessException )
				{
				}
			}
		}

		// reads the json that claude -p --output-format json writes
		static Reply ParseReply( string output )
		{
			// skip anything printed before or after the json object
			var start = output.IndexOf( '{' );
			var end = output.LastIndexOf( '}' );

			if ( ( start < 0 ) || ( end <= start ) )
			{
				throw new InvalidOperationException( "claude did not print a JSON result: " + Truncate( output ) );
			}

			using ( var document = JsonDocument.Parse( output.Substring( start, end - start + 1 ) ) )
			{
				var root = document.RootElement;

				var resultText = ( root.TryGetProperty( "result", out var resultElement ) && ( resultElement.ValueKind == JsonValueKind.String ) ) ? resultElement.GetString() ?? "" : "";

				if ( root.TryGetProperty( "is_error", out var isError ) && ( isError.ValueKind == JsonValueKind.True ) )
				{
					throw new InvalidOperationException( "claude reported an error: " + Truncate( resultText ) );
				}

				// the answer that matched the schema
				if ( root.TryGetProperty( "structured_output", out var structured ) && ( structured.ValueKind == JsonValueKind.Object ) )
				{
					return ReadStructuredReply( structured );
				}

				// an older command line may only give the text: read it as json if it is, or as the final answer if it is not
				try
				{
					using ( var inner = JsonDocument.Parse( resultText ) )
					{
						if ( inner.RootElement.ValueKind == JsonValueKind.Object )
						{
							return ReadStructuredReply( inner.RootElement );
						}
					}
				}
				catch ( JsonException )
				{
				}

				return new Reply { m_text = resultText };
			}
		}

		// reads the object that matches c_responseSchema
		static Reply ReadStructuredReply( JsonElement element )
		{
			var reply = new Reply();

			if ( element.TryGetProperty( "text", out var text ) && ( text.ValueKind == JsonValueKind.String ) )
			{
				reply.m_text = text.GetString() ?? "";
			}

			if ( !element.TryGetProperty( "tool_calls", out var toolCalls ) || ( toolCalls.ValueKind != JsonValueKind.Array ) )
			{
				return reply;
			}

			foreach ( var toolCall in toolCalls.EnumerateArray() )
			{
				if ( !toolCall.TryGetProperty( "name", out var name ) || ( name.ValueKind != JsonValueKind.String ) )
				{
					continue;
				}

				var call = new ToolCall { m_name = name.GetString() ?? "" };

				if ( toolCall.TryGetProperty( "arguments", out var arguments ) && ( arguments.ValueKind == JsonValueKind.Object ) )
				{
					foreach ( var property in arguments.EnumerateObject() )
					{
						// semantic kernel converts a string to the parameter's type, so every value goes in as text
						call.m_arguments[ property.Name ] = ( property.Value.ValueKind == JsonValueKind.String ) ? property.Value.GetString() : property.Value.GetRawText();
					}
				}

				reply.m_toolCalls.Add( call );
			}

			return reply;
		}

		// runs the claude process with the prompt on standard input, and returns its standard output
		async Task<string> RunProcessAsync( List<string> arguments, string standardInput, string workingDirectory, CancellationToken cancellationToken )
		{
			var startInfo = CreateStartInfo( arguments );

			startInfo.WorkingDirectory = workingDirectory;
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.RedirectStandardInput = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardError = true;
			startInfo.StandardOutputEncoding = new UTF8Encoding( false );
			startInfo.StandardErrorEncoding = new UTF8Encoding( false );

			using ( var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true } )
			{
				var exited = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );

				process.Exited += ( sender, args ) => exited.TrySetResult( true );

				try
				{
					process.Start();
				}
				catch ( System.ComponentModel.Win32Exception exception )
				{
					throw new InvalidOperationException( "Could not start '" + startInfo.FileName + "'. Install Claude Code, or set the path of the claude executable on the MetagameOrchestrator. " + exception.Message, exception );
				}

				// read both streams from the start, so a full pipe cannot block the process
				var standardOutputTask = process.StandardOutput.ReadToEndAsync();
				var standardErrorTask = process.StandardError.ReadToEndAsync();

				// send the prompt and close standard input, which tells claude the prompt is complete
				try
				{
					using ( var writer = new StreamWriter( process.StandardInput.BaseStream, new UTF8Encoding( false ) ) )
					{
						await writer.WriteAsync( standardInput ).ConfigureAwait( false );
					}
				}
				catch ( IOException )
				{
					// claude ended before it read the prompt (a bad argument, not signed in); its exit code and standard error say why
				}

				// wait for the process to end, the timeout, or a cancel
				using ( var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken ) )
				{
					timeoutSource.CancelAfter( m_options.m_timeout );

					var delayTask = Task.Delay( Timeout.Infinite, timeoutSource.Token );

					if ( process.HasExited )
					{
						exited.TrySetResult( true );
					}

					var finishedTask = await Task.WhenAny( exited.Task, delayTask ).ConfigureAwait( false );

					if ( finishedTask != exited.Task )
					{
						KillProcessTree( process );

						cancellationToken.ThrowIfCancellationRequested();

						throw new TimeoutException( "claude did not answer within " + m_options.m_timeout.TotalSeconds.ToString( CultureInfo.InvariantCulture ) + " seconds." );
					}

					// end the delay, so it does not wait on a disposed token source
					timeoutSource.Cancel();
				}

				// make sure the output has been read to the end
				process.WaitForExit();

				var standardOutput = await standardOutputTask.ConfigureAwait( false );
				var standardError = await standardErrorTask.ConfigureAwait( false );

				if ( ( process.ExitCode != 0 ) && string.IsNullOrWhiteSpace( standardOutput ) )
				{
					throw new InvalidOperationException( "claude exited with code " + process.ExitCode.ToString( CultureInfo.InvariantCulture ) + ": " + Truncate( standardError ) );
				}

				return standardOutput;
			}
		}

		// works out how to start claude
		ProcessStartInfo CreateStartInfo( List<string> arguments )
		{
			var executable = ResolveExecutable( m_options.m_executable );

			var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

			// a batch file (the npm install of claude) has to go through cmd.exe
			var extension = Path.GetExtension( executable );

			if ( isWindows && ( string.Equals( extension, ".cmd", StringComparison.OrdinalIgnoreCase ) || string.Equals( extension, ".bat", StringComparison.OrdinalIgnoreCase ) ) )
			{
				foreach ( var argument in arguments )
				{
					// cmd.exe expands these even inside quotes
					var expanded = argument.IndexOfAny( new[] { '%', '!', '\r', '\n' } ) >= 0;

					// every argument is quoted below, but an escaped quote inside one ends cmd.exe's quoting, so these must not follow it
					var unquoted = ( argument.IndexOf( '"' ) >= 0 ) && ( argument.IndexOfAny( new[] { '&', '|', '<', '>', '(', ')', '^' } ) >= 0 );

					if ( expanded || unquoted )
					{
						throw new InvalidOperationException( "An argument for claude holds a character that cmd.exe would change. Use claude.exe (the native install) instead of claude.cmd, or move the working folder to a path without such characters." );
					}
				}

				var comSpec = Environment.GetEnvironmentVariable( "ComSpec" );

				return new ProcessStartInfo( string.IsNullOrEmpty( comSpec ) ? "cmd.exe" : comSpec, "/d /s /c \"" + QuoteWindowsArgument( executable, true ) + " " + BuildCommandLine( arguments, true, true ) + "\"" );
			}

			return new ProcessStartInfo( executable, BuildCommandLine( arguments, isWindows, false ) );
		}

		// finds the claude executable: a full path as is, otherwise on the PATH, then in the folders the installers use
		static string ResolveExecutable( string executable )
		{
			if ( string.IsNullOrEmpty( executable ) )
			{
				executable = "claude";
			}

			if ( Path.IsPathRooted( executable ) )
			{
				return executable;
			}

			var isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

			var extensions = ( isWindows && string.IsNullOrEmpty( Path.GetExtension( executable ) ) ) ? new[] { ".exe", ".cmd", ".bat" } : new[] { "" };

			var folders = new List<string>();

			var path = Environment.GetEnvironmentVariable( "PATH" );

			if ( !string.IsNullOrEmpty( path ) )
			{
				folders.AddRange( path.Split( Path.PathSeparator ) );
			}

			// the native installer and npm put claude here, which an editor started before the install may not have on its PATH
			var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );

			if ( !string.IsNullOrEmpty( home ) )
			{
				folders.Add( Path.Combine( home, ".local", "bin" ) );
				folders.Add( Path.Combine( home, ".claude", "local" ) );
			}

			var appData = Environment.GetFolderPath( Environment.SpecialFolder.ApplicationData );

			if ( isWindows && !string.IsNullOrEmpty( appData ) )
			{
				folders.Add( Path.Combine( appData, "npm" ) );
			}

			foreach ( var extension in extensions )
			{
				foreach ( var folder in folders )
				{
					if ( string.IsNullOrWhiteSpace( folder ) )
					{
						continue;
					}

					string candidate;

					try
					{
						candidate = Path.Combine( folder.Trim().Trim( '"' ), executable + extension );
					}
					catch ( ArgumentException )
					{
						continue;
					}

					if ( File.Exists( candidate ) )
					{
						return candidate;
					}
				}
			}

			// let the operating system try
			return executable;
		}

		// joins arguments into one command line
		static string BuildCommandLine( List<string> arguments, bool isWindows, bool alwaysQuote )
		{
			var builder = new StringBuilder();

			foreach ( var argument in arguments )
			{
				if ( builder.Length > 0 )
				{
					builder.Append( ' ' );
				}

				builder.Append( isWindows ? QuoteWindowsArgument( argument, alwaysQuote ) : QuotePosixArgument( argument ) );
			}

			return builder.ToString();
		}

		// quotes one argument so that CommandLineToArgvW (and the C runtime) give it back unchanged
		public static string QuoteWindowsArgument( string argument, bool alwaysQuote )
		{
			if ( argument == null )
			{
				argument = "";
			}

			// no quotes needed for a plain word
			if ( !alwaysQuote && ( argument.Length > 0 ) && ( argument.IndexOfAny( new[] { ' ', '\t', '\n', '\v', '"' } ) < 0 ) )
			{
				return argument;
			}

			var builder = new StringBuilder();

			builder.Append( '"' );

			var backslashes = 0;

			foreach ( var character in argument )
			{
				if ( character == '\\' )
				{
					backslashes++;

					continue;
				}

				if ( character == '"' )
				{
					// backslashes before a quote are doubled, and the quote is escaped
					builder.Append( '\\', ( backslashes * 2 ) + 1 );
				}
				else
				{
					// backslashes anywhere else stay as they are
					builder.Append( '\\', backslashes );
				}

				backslashes = 0;

				builder.Append( character );
			}

			// backslashes before the closing quote are doubled
			builder.Append( '\\', backslashes * 2 );

			builder.Append( '"' );

			return builder.ToString();
		}

		// quotes one argument for mono's argument splitter on linux and macos
		static string QuotePosixArgument( string argument )
		{
			if ( argument == null )
			{
				argument = "";
			}

			return "\"" + argument.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" ) + "\"";
		}

		// ends the process, and on windows the processes it started (cmd.exe starts node for claude.cmd)
		static void KillProcessTree( Process process )
		{
			try
			{
				if ( process.HasExited )
				{
					return;
				}

				if ( Environment.OSVersion.Platform == PlatformID.Win32NT )
				{
					using ( var taskKill = Process.Start( new ProcessStartInfo( "taskkill", "/PID " + process.Id.ToString( CultureInfo.InvariantCulture ) + " /T /F" ) { CreateNoWindow = true, UseShellExecute = false } ) )
					{
						taskKill?.WaitForExit( 5000 );
					}
				}

				if ( !process.HasExited )
				{
					process.Kill();
				}
			}
			catch ( InvalidOperationException )
			{
				// it ended on its own
			}
			catch ( System.ComponentModel.Win32Exception )
			{
				// it could not be ended; the timeout is still reported
			}
		}

		// shortens text for an error message
		static string Truncate( string text )
		{
			const int c_maximumLength = 500;

			if ( string.IsNullOrEmpty( text ) )
			{
				return "(nothing)";
			}

			return ( text.Length <= c_maximumLength ) ? text : text.Substring( 0, c_maximumLength ) + "...";
		}
	}
}
