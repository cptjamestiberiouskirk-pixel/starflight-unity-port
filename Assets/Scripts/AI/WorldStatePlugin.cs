
using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Microsoft.SemanticKernel;
using UnityEngine;

namespace Starflight.AI
{
	// kernel functions that let the language model change the state of the galaxy
	//
	// semantic kernel runs these on a thread pool thread, so they never touch a unity object directly:
	// everything that has to happen in unity goes through MainThreadDispatcher.Enqueue
	public sealed class WorldStatePlugin
	{
		// the outcomes an event between two factions can have
		static readonly string[] c_outcomes =
		{
			"an uneasy ceasefire",
			"a skirmish at the border",
			"the annexation of a contested system",
			"a trade pact for endurium",
			"a stalemate of patrols",
			"a raid on a supply convoy",
		};

		// how far an event reaches
		static readonly string[] c_intensities =
		{
			"barely noticed",
			"local",
			"sector wide",
			"felt across the galaxy",
			"the start of a war",
		};

		// counts events, so two events between the same factions do not always end the same way
		int m_eventCounter;

		// raised on the main thread with the text of every event (for a ship's log or a debug panel)
		public event Action<string> FactionEventLogged;

		[KernelFunction( "generate_faction_event" )]
		[Description( "Resolves one event between two factions of the Starflight galaxy (for example the Thrynn, the Elowan, the Veloxi, the Spemin, the Mechans, the Gazurtoid, the Uhlek or the humans of Arth) and records it in the game's world state. Call this whenever the story needs the outcome of a dispute, a battle or a treaty between two factions: it decides the outcome, so do not invent one yourself. Returns the outcome and how far it reaches, as a sentence to build the narration on." )]
		public string GenerateFactionEvent(
			[Description( "The name of the first faction, the one that starts the event, for example \"Thrynn\"." )] string factionA,
			[Description( "The name of the second faction, the one the event is aimed at, for example \"Elowan\"." )] string factionB,
			[Description( "What kind of event this is, in a few words, for example \"border dispute\" or \"trade talks\". Optional." )] string eventKind = "border dispute" )
		{
			// guard against empty names
			factionA = string.IsNullOrWhiteSpace( factionA ) ? "an unknown faction" : factionA.Trim();
			factionB = string.IsNullOrWhiteSpace( factionB ) ? "an unknown faction" : factionB.Trim();
			eventKind = string.IsNullOrWhiteSpace( eventKind ) ? "border dispute" : eventKind.Trim();

			// the outcome comes from the names and the event number, so the same request in the same order gives the same answer
			var eventNumber = Interlocked.Increment( ref m_eventCounter );

			var hash = Fnv1a( factionA.ToUpperInvariant() + "|" + factionB.ToUpperInvariant() + "|" + eventKind.ToUpperInvariant() + "|" + eventNumber.ToString( CultureInfo.InvariantCulture ) );

			var outcome = c_outcomes[ (int) ( hash % (uint) c_outcomes.Length ) ];

			var intensity = c_intensities[ (int) ( ( hash / (uint) c_outcomes.Length ) % (uint) c_intensities.Length ) ];

			var summary = string.Format( CultureInfo.InvariantCulture, "Faction event {0}: the {1} between the {2} and the {3} ended in {4} ({5}).", eventNumber, eventKind, factionA, factionB, outcome, intensity );

			// log it on the main thread
			MainThreadDispatcher.Enqueue( () =>
			{
				Debug.Log( "[WorldState] " + summary );

				FactionEventLogged?.Invoke( summary );
			} );

			// the language model gets the same text back
			return summary;
		}

		// a stable 32 bit hash (string.GetHashCode can differ between runs)
		static uint Fnv1a( string text )
		{
			var hash = 2166136261u;

			foreach ( var character in text )
			{
				hash ^= character;
				hash *= 16777619u;
			}

			return hash;
		}
	}
}
