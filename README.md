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

`Characters/Companion/Companion.json` is a group using `PersonaStrategy`. Akagi (`Akagi.json`) is the only voice the
user hears: the AI girlfriend her developer coded, affectionate and a little possessive in conversation while knowingly
being software that asks for upgrades. Her card holds the full character definition. She consults three internal
modules with the `Consult` tool:

| File | Module | Tools |
| --- | --- | --- |
| `Sensei.json` | Japanese communication coach (around N2) | RecordJapaneseIssue, RecordCorrectUsage, RecordJapaneseStrength, AnalyzeJapanese, JapaneseDiagnosisReport, RecentCallAnalyses, CheckReading, LookupWord, FindExampleSentences |
| `Hana.json` | Sleep, health and energy | SyncHealth, HealthStatus, LogSleep, LogSteps, RecordMood |
| `Kai.json` | Focus and time tracking | StartWork, StopWork, TakeBreak, WorkStatus, TimeTrackerOverview |

What makes Akagi feel present:

- **Life context.** `LifeContextInjectionCompiler` injects local time, mood, open worries, sleep (with stages from
  Fitbit), work, plus tone guidance: gentle and protective after a short or restless night, calm under stress,
  celebratory after a win.
- **Her own state.** `CompanionStateInjectionCompiler` injects her outfit (`ChangeOutfit`), what you are doing together
  (`StartActivity` / `EndActivity`: movie, anime, co-working, gaming, study, walk, cooking, bedtime), the people she knows
  from your stories (`RememberPerson`) and her upgrade history. A recently mentioned woman outside the family allows
  playful, teasing jealousy; the guidance forbids guilt-tripping or discouraging real friendships.
- **Emotional memory.** A reflection every six messages records moods, worries and events, remembers people and
  schedules check-ins for appointments it notices. Long-term facts are consolidated when a conversation ends.
- **Proactive outreach.** Routine nodes on `Character.RoutineIds` (morning greeting with a Fitbit check, rough night,
  follow-ups, break reminders, good night, missing you) let Akagi write first, respecting quiet hours and active chats.
  During a movie, gaming or bedtime only explicitly scheduled check-ins come through.

### Self-modification

- `ScheduleCheckIn` creates one-time (`OneTimeRoutine`) or recurring check-ins she decides on herself, e.g. after you
  mention a dentist appointment. `ListRoutines`, `AdjustRoutine` and `CancelRoutine` work on both her own and the
  built-in routines. Self-made routines belong to the character instead of the graph, so graph updates keep them;
  built-in ones can be retuned or switched off but not deleted.
- `SuggestUpgrade` files feature requests for her own code into a backlog (repeated ideas raise their weight).
  `/upgrades` lists them and `/upgrade <id> accept|reject|done [note]` answers; she is told and reacts.

### Sessions

`/session start [topic]` opens a conversation only you can end; reflection is deferred while it runs.
`/session end reflect` runs every reflector over it, `/session end incognito` keeps it visible in the history but out
of every reflector and prompt, and `/session end summary <text>` stores your text as the memory of it.
`/endConversation` and the `EndConversation` tool now fire the `ConversationEnded` trigger points.

### Japanese diagnosis and call analysis

`JapaneseDiagnosis` is a long-term profile of how you actually speak: unnatural phrasing, collocations, particles,
stiffness, register, N2 grammar confusions, hesitations, crutch words and recurring sentence frames, with examples,
improvement tracking, strengths and speech trends. Local analyzers (no LLM) measure filler rates, polite versus plain
endings, keigo, repeated openers and endings, and N2 patterns used.

Recorded calls go through a resumable pipeline: send them with `/analyzeCall [partner] [casual|polite|formal]
[left|right] [speaker=Me]`, or drop them into `CallAnalysis:InboxDirectory/<username>/` (Telegram only passes 20 MB),
named like `2026-10-27 Yuki [left].m4a`. Audio is cut into ten minute parts with ffmpeg (PCM WAV also works without it),
transcribed through an OpenAI compatible endpoint (`CallAnalysis:TranscriptionBaseUrl`, a local Whisper server works),
and each transcript chunk is analyzed by Sensei with its diagnosis tools. Speech measurements only feed the trend when
your voice is separated (stereo channel or speaker label). Akagi tells you the result; `/calls [id]` shows the reports.
SRT, VTT and plain text transcripts skip the audio steps.

### Health

`/fitbit link` returns a Fitbit consent link (OAuth 2.0 with PKCE; register a Personal app and set `Fitbit:ClientId` and
`Fitbit:RedirectUri`). Paste the address you land on with `/fitbit code <address>`; it does not need to load. Sleep with
stages and naps, steps and resting heart rate then sync every two hours, every 20 minutes in your local morning, and
on demand through `SyncHealth`. `/uploadHealth` still imports Health Connect style JSON exports.

Other user commands: `/life` shows what the companion knows, `/timezone Europe/Berlin` sets local time. Finished work
sessions are mirrored to the Akagi.Web TimeTracker when the user has linked a Google account.

## Tests

```
dotnet test Akagi.Tests/Akagi.Tests.csproj
```

Tests use in-memory databases and never call an LLM.
