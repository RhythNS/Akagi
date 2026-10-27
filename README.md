# Akagi

Akagi is a personal AI companion: characters and their cognitive pipelines are behavior graphs
(`Akagi.CharacterEditor/Characters/*.json`) that the server instantiates per user.

## Graph tooling (headless)

`Akagi.CharacterEditor.exe` opens the editor when started without arguments. With dashed arguments it runs
headlessly, prints to stdout/stderr and returns an exit code (0 ok, 1 failed, 2 usage error):

```
Akagi.CharacterEditor.exe --validate <graph.json|directory>... [--json] [--strict] [--search <dir>]
Akagi.CharacterEditor.exe --dump-nodes [nodes.json] [--text]
Akagi.CharacterEditor.exe --describe-node <TypeName>
Akagi.CharacterEditor.exe --compose <graph.json> [-o flattened.json]
```

- `--validate` reports unknown or renamed node types, invalid property values, dangling and mistyped
  connections, empty array slots, unconnected required inputs, unknown LLM command names, orphans,
  unresolvable graph references and GraphIds shared by several files.
- `--dump-nodes` describes every node type (properties, defaults, enum values, inputs and the node types
  that fit them) and every LLM command with its arguments.

## Composing graphs

Graph reference nodes (`CharacterGraphReference`, `ReflectorGraphReference`, `TriggerPointGraphReference`,
`MessageCompilerGraphReference`, ...) stand for a node exported by another graph file:

- `GraphPath` is relative to the referencing file; `GraphId` finds a graph uploaded before.
- `ExportNode` picks the node by Name or `#index`; when empty, the single root node of the exported type is used.
- `Overrides` such as `Akagi.Name=Aki` or `#3.MaxSteps=5` change properties of the referenced graph.

References are inlined before instantiation, so a five node group graph can combine several characters that each
live in their own file. `/uploadGraph` stores every uploaded file, so references resolve against graphs uploaded
earlier or in the same message.

## The companion

`Characters/Companion/Companion.json` is a group using `PersonaStrategy`: Akagi (`Akagi.json`) is the only voice
the user hears, and consults three advisors with the `Consult` tool:

| File | Role | Tools |
| --- | --- | --- |
| `Sensei.json` | Japanese tutor (JLPT N2) | LookupWord, FindExampleSentences, CheckReading, vocabulary deck, grammar mistakes, study log |
| `Hana.json` | Sleep, health and energy | HealthStatus, LogSleep, LogSteps, RecordMood |
| `Kai.json` | Focus and time tracking | StartWork, StopWork, TakeBreak, WorkStatus, TimeTrackerOverview |

What makes Akagi feel present:

- **Life context.** `LifeContextInjectionCompiler` injects local time, mood, open worries, sleep, work and study
  progress, plus tone guidance (gentle after a short night, calm under stress, celebratory after a win).
- **Emotional memory.** `RecordMood` / `ResolveConcern` keep a mood journal with worries and events to follow up on;
  a mood reflection runs every few messages.
- **Proactive outreach.** Routine nodes on `Character.RoutineIds` (morning greeting, rough night, follow-ups,
  break reminders, study streak, inactivity) let Akagi write first, respecting quiet hours and active chats.
  For a group persona, outreach is delivered through the group so it shares one conversation.

User commands: `/life` shows what the companion knows, `/timezone Europe/Berlin` sets local time, and
`/uploadHealth` imports a Health Connect style JSON export (sleep sessions, steps, resting heart rate). Finished
work sessions are mirrored to the Akagi.Web TimeTracker when the user has linked a Google account.

## Tests

```
dotnet test Akagi.Tests/Akagi.Tests.csproj
```

Tests use in-memory databases and never call an LLM.
