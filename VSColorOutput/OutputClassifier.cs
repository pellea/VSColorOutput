using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

// ReSharper disable EmptyGeneralCatchClause
#pragma warning disable 67

namespace BlueOnionSoftware
{
    public class OutputClassifier : IClassifier
    {
        private bool _settingsLoaded;
        private IEnumerable<Classifier> _classifiers;
        private readonly IClassificationTypeRegistryService _classificationTypeRegistry;
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        public OutputClassifier(IClassificationTypeRegistryService registry)
        {
            try
            {
                _classificationTypeRegistry = registry;
                Settings.SettingsUpdated += (sender, args) => _settingsLoaded = false;
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                throw;
            }
        }

        private struct Classifier
        {
            public string Type { get; set; }
            public Predicate<string> Test { get; set; }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            try
            {
                var spans = new List<ClassificationSpan>();
                var snapshot = span.Snapshot;
                if (snapshot == null || snapshot.Length == 0)
                {
                    return spans;
                }
                LoadSettings();
                var start = span.Start.GetContainingLine().LineNumber;
                var end = (span.End - 1).GetContainingLine().LineNumber;
                for (var i = start; i <= end; i++)
                {
                    var line = snapshot.GetLineFromLineNumber(i);
                    var snapshotSpan = new SnapshotSpan(line.Start, line.Length);
                    var text = line.Snapshot.GetText(snapshotSpan);

                    if (string.IsNullOrEmpty(text) == false)
                    {
                        var classificationName = _classifiers.First(classifier => classifier.Test(text)).Type;
                        var type = _classificationTypeRegistry.GetClassificationType(classificationName);
                        spans.Add(new ClassificationSpan(line.Extent, type));
                    }
                }
                return spans;
            }
            catch (RegexMatchTimeoutException)
            {
                // eat it.
                return new List<ClassificationSpan>();
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                throw;
            }
        }

        private void LoadSettings()
        {
            if (_settingsLoaded) return;

            var settings = Settings.Load();
            var patterns = settings.Patterns ?? new RegExClassification[0];

            var classifiers = patterns.Select(
                pattern => new
                {
                    pattern,
                    test = new Regex(
                        pattern.RegExPattern,
                        pattern.IgnoreCase
                            ? RegexOptions.IgnoreCase
                            : RegexOptions.None,
                        TimeSpan.FromMilliseconds(250))
                })
                .Select(pattern => new Classifier
                {
                    Type = pattern.pattern.ClassificationType.ToString(),
                    Test = text => pattern.test.IsMatch(text)
                })
                .ToList();

            classifiers.Add(new Classifier
            {
                Type = OutputClassificationDefinitions.BuildText,
                Test = t => true
            });

            _classifiers = classifiers;
            _settingsLoaded = true;
        }

        public static void LogError(string message)
        {
            try
            {
                // I'm co-opting the Visual Studio event source because I can't register
                // my own from a .VSIX installer.
                EventLog.WriteEntry("Microsoft Visual Studio",
                    "VSColorOutput: " + (message ?? "null"),
                    EventLogEntryType.Error);
            }
            catch
            {
                // Don't kill extension on logging errors
            }
        }
    }
}