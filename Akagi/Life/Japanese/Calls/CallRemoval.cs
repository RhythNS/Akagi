using Akagi.Life.Japanese.Mining;
using Microsoft.Extensions.Logging;

namespace Akagi.Life.Japanese.Calls;

/// <summary>
/// What deleting a call took away, for the log and the page.
/// </summary>
internal record CallRemovalResult(int Diagnosis, int Facts, bool FilesDeleted, int MinedSentences);

/// <summary>
/// Deletes a call and takes back everything it added: its mistakes, correct uses and strengths in the diagnosis, its
/// speech snapshot and grammar counts, the facts on the partner's page, the companion's people notes and memories, the
/// partner's call entry, the sentences mined from it, and its audio and transcripts on disk.
/// </summary>
internal class CallRemoval
{
    private readonly ICallJobDatabase _jobs;
    private readonly IJapaneseDiagnosisDatabase _diagnoses;
    private readonly IPersonProfileDatabase _people;
    private readonly ICallMemory _memory;
    private readonly IMinedSentenceDatabase? _mined;
    private readonly string _storageDirectory;
    private readonly ILogger _logger;

    public CallRemoval(ICallJobDatabase jobs, IJapaneseDiagnosisDatabase diagnoses, IPersonProfileDatabase people, ICallMemory memory,
        IMinedSentenceDatabase? mined, string storageDirectory, ILogger logger)
    {
        _jobs = jobs;
        _diagnoses = diagnoses;
        _people = people;
        _memory = memory;
        _mined = mined;
        _storageDirectory = storageDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Null when the call cannot be deleted yet, with the reason in <paramref name="error"/>.
    /// </summary>
    public async Task<CallRemovalResult?> RemoveAsync(CallJob job, Action<string>? error = null)
    {
        if (!job.IsFinished)
        {
            // The analysis service owns a running job and would save it again
            error?.Invoke("A call can be deleted once its analysis is finished or has failed.");
            return null;
        }

        int diagnosisChanges = 0;
        int facts = 0;
        JapaneseDiagnosis? diagnosis = await _diagnoses.GetForUser(job.UserId);
        foreach (CallFinding finding in job.Findings.Where(f => f.Applied).ToList())
        {
            if (finding.Kind == FindingKind.Fact)
            {
                await _memory.UndoAsync(job, finding);
                facts++;
            }
            else if (diagnosis != null)
            {
                CallDiagnosis.Undo(diagnosis, job, finding);
                diagnosisChanges++;
            }
        }
        if (diagnosis != null)
        {
            diagnosisChanges += UndoMeasurements(diagnosis, job);
            await _diagnoses.SaveDocumentAsync(diagnosis);
        }

        await RemoveFromPersonAsync(job);
        int mined = _mined == null ? 0 : await _mined.DeleteForCallAsync(job.UserId, job.Id!);
        bool filesDeleted = DeleteFiles(job);
        await _jobs.DeleteDocumentByIdAsync(job.Id!);

        _logger.LogInformation("Deleted call {JobId}: {Diagnosis} diagnosis entries and {Facts} facts taken back, {Mined} mined sentences, files deleted: {Files}",
            job.Id, diagnosisChanges, facts, mined, filesDeleted);
        return new CallRemovalResult(diagnosisChanges, facts, filesDeleted, mined);
    }

    /// <summary>
    /// The report stage counted the call's N2 grammar and, with separate voices, saved one speech snapshot. Measured again
    /// from the transcript, so exactly that much is taken back.
    /// </summary>
    internal static int UndoMeasurements(JapaneseDiagnosis diagnosis, CallJob job)
    {
        if (job.Status != CallJobStatus.Completed || job.Segments.Count == 0)
        {
            return 0;
        }
        int changes = 0;
        SpeechReport report = SpeechAnalysis.Analyze(job.LearnerText(), job.Register);
        foreach (PatternMatch match in report.Patterns)
        {
            PatternUsage? usage = diagnosis.PatternUsage.FirstOrDefault(u => u.Pattern == match.Pattern.Key);
            if (usage == null)
            {
                continue;
            }
            usage.Count = Math.Max(0, usage.Count - match.Count);
            if (usage.Count == 0)
            {
                diagnosis.PatternUsage.Remove(usage);
            }
            changes++;
        }

        string source = job.SourceLabel;
        int index = diagnosis.Snapshots.FindIndex(s => s.Source == source && s.Characters == report.Fillers.JapaneseCharacters);
        if (index < 0)
        {
            index = diagnosis.Snapshots.FindLastIndex(s => s.Source == source);
        }
        if (job.HasLearnerSeparation && index >= 0)
        {
            diagnosis.Snapshots.RemoveAt(index);
            changes++;
        }
        if (changes > 0)
        {
            diagnosis.Dirty = true;
        }
        return changes;
    }

    private async Task RemoveFromPersonAsync(CallJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Partner) || await _people.Get(job.UserId, job.Partner) is not PersonProfile profile)
        {
            return;
        }
        int removed = profile.Calls.RemoveAll(c => c.CallId == job.Id) + profile.Facts.RemoveAll(f => f.CallId == job.Id);
        if (removed == 0)
        {
            return;
        }
        if (profile.Calls.Count == 0 && profile.Facts.Count == 0 && profile.Id != null)
        {
            await _people.DeleteDocumentByIdAsync(profile.Id);
            return;
        }
        profile.Dirty = true;
        await _people.SaveDocumentAsync(profile);
    }

    /// <summary>
    /// The call's folder holds the recording, its audio parts and cached transcriptions. Only a folder inside the
    /// storage directory and named after the call is deleted.
    /// </summary>
    private bool DeleteFiles(CallJob job)
    {
        if (string.IsNullOrEmpty(job.Id) || string.IsNullOrWhiteSpace(_storageDirectory))
        {
            return false;
        }
        string root = Path.GetFullPath(_storageDirectory);
        string folder = Path.GetFullPath(Path.Combine(root, job.Id));
        if (!folder.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(folder))
        {
            return false;
        }
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the files of call {JobId} in {Folder}", job.Id, folder);
            return false;
        }
    }
}
