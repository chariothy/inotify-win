using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;

namespace De.Thekid.INotify
{
    // List of possible changes
    public enum Change
    {
        CREATE, MODIFY, DELETE, MOVED_FROM, MOVED_TO
    }

    /// Main class
    public class Runner
    {
        // Mappings
        protected static Dictionary<WatcherChangeTypes, Change> Changes = new Dictionary<WatcherChangeTypes, Change>();

        private List<Thread> _threads = new List<Thread>();
        private bool _stopMonitoring = false;
        private ManualResetEventSlim _stopMonitoringEvent;
        private object _notificationReactionLock = new object();
        private Arguments _args = null;

        private readonly Dictionary<string, DateTime> m_pendingEvents = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, FileSystemEventArgs> m_pendingTypes = new Dictionary<string, FileSystemEventArgs>();
        private readonly Timer m_timer;
        private bool m_timerStarted = false;

        static Runner()
        {
            Changes[WatcherChangeTypes.Created]= Change.CREATE;
            Changes[WatcherChangeTypes.Changed]= Change.MODIFY;
            Changes[WatcherChangeTypes.Deleted]= Change.DELETE;
        }

        public Runner(Arguments args)
        {
            _args = args;

            m_timer = new Timer(OnTimeout, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// Callback for errors in watcher
        protected void OnWatcherError(object source, ErrorEventArgs e)
        {
            Console.Error.WriteLine("*** {0}", e.GetException());
        }
        
        private void OnWatcherNotification(object sender, FileSystemEventArgs e)
        {
            lock (m_pendingEvents)
            {
                // if only looking for one change and another thread beat us to it, return
                if (!_args.Monitor && _stopMonitoring)
                {
                    return;
                }
                if (null != _args.Exclude && _args.Exclude.IsMatch(e.FullPath))
                {
                    return;
                }

                // Save a timestamp for the most recent event for this path  
                String key = e.FullPath + " " + e.ChangeType;
                m_pendingEvents[key] = DateTime.Now;
                m_pendingTypes[key] = e;
                
                // Start a timer if not already started  
                if (!m_timerStarted)
                {
                    m_timer.Change(100, 100);
                    m_timerStarted = true;
                }
            }
        }

        private void OnTimeout(object state)
        {
            List<string> paths;

            // Don't want other threads messing with the pending events right now  
            lock (m_pendingEvents)
            {
                // Get a list of all paths that should have events thrown  
                paths = FindReadyPaths(m_pendingEvents);

                // Remove paths that are going to be used now  
                paths.ForEach(delegate (string path)
                {
                    m_pendingEvents.Remove(path);
                });

                // Stop the timer if there are no more events pending  
                if (m_pendingEvents.Count == 0)
                {
                    m_timer.Change(Timeout.Infinite, Timeout.Infinite);
                    m_timerStarted = false;
                }
            }

            // Fire an event for each path that has changed  
            paths.ForEach(delegate (string path)
            {
                FileSystemEventArgs e = m_pendingTypes[path];
                m_pendingTypes.Remove(path);
                Action outputAction;
                if(e.ChangeType == WatcherChangeTypes.Renamed)
                {
                    RenamedEventArgs er = e as RenamedEventArgs;
                    outputAction = () =>
                    {
                        Output(Console.Out, _args.Format, e, Change.MOVED_FROM, er.OldName);
                        Output(Console.Out, _args.Format, e, Change.MOVED_TO, er.Name);
                    };
                }
                else
                {
                    outputAction = () => Output(Console.Out, _args.Format, e, Changes[e.ChangeType], e.Name);
                }
                outputAction();
                if(!String.IsNullOrWhiteSpace(_args.Execute))
                {
                    Execute(Console.Out);
                }                
            });

            // If only looking for one change, signal to stop
            if (!_args.Monitor)
            {
                _stopMonitoring = true;
                _stopMonitoringEvent.Set();
            }
        }

        private List<string> FindReadyPaths(Dictionary<string, DateTime> events)
        {
            List<string> results = new List<string>();
            DateTime now = DateTime.Now;

            foreach (KeyValuePair<string, DateTime> entry in events)
            {
                // If the path has not received a new event in the last 75ms  
                // an event for the path should be fired  
                double diff = now.Subtract(entry.Value).TotalMilliseconds;
                if (diff >= 75)
                {
                    results.Add(entry.Key);
                }
            }

            return results;
        }

        /// Output method
        protected void Output(TextWriter writer, string[] tokens, FileSystemEventArgs source, Change type, string name)
        {
            writer.Write(DateTime.Now);
            writer.Write(' ');
            foreach (var token in tokens)
            {
                var path = Path.Combine(source.FullPath, name);
                switch (token[0])
                {
                    case 'e':
                        writer.Write(type);
                        if (Directory.Exists(path))
                        {
                            writer.Write(",ISDIR");
                        }
                        break;
                    case 'f': writer.Write(Path.GetFileName(path)); break;
                    case 'w': writer.Write(Path.Combine(source.FullPath, Path.GetDirectoryName(path))); break;
                    case 'T': writer.Write(DateTime.Now); break;
                    default: writer.Write(token); break;
                }
            }
            writer.WriteLine();
        }

        protected void Execute(TextWriter writer)
        {
            string cmd = String.Format("{0} {1}", _args.Execute, _args.Parameter);
            writer.WriteLine("===> Begin job : " + cmd);

            System.Diagnostics.Process exep = new System.Diagnostics.Process();
            exep.StartInfo.FileName = _args.Execute;
            exep.StartInfo.Arguments = _args.Parameter;
            exep.StartInfo.CreateNoWindow = true;
            exep.StartInfo.UseShellExecute = false;
            exep.StartInfo.RedirectStandardError = true;
            exep.StartInfo.RedirectStandardOutput = true;
            exep.ErrorDataReceived += (obj, p) =>
            {
                if (string.IsNullOrEmpty(p.Data) == false)
                {
                    writer.WriteLine(p.Data);
                }
            };

            exep.OutputDataReceived += (obj, p) =>
            {
                if (string.IsNullOrEmpty(p.Data) == false)
                {
                    writer.WriteLine(p.Data);
                }   
            };
            exep.Start();
            exep.BeginOutputReadLine();
            exep.BeginErrorReadLine();
            
            bool result = exep.WaitForExit(_args.Timeout * 1000);
            writer.WriteLine(String.Format("===> End job : {0}, result = {1}", cmd, result));
        }

        public void Processor(object data)
        {
            string path = (string)data;

            string fileName = "*.*";
            if (File.Exists(path))
            {
                fileName = Path.GetFileName(path);
                path = Path.GetDirectoryName(path);
            }
            using (var w = new FileSystemWatcher {
                Path = path,
                IncludeSubdirectories = _args.Recursive,
                Filter = fileName
            }) {
                w.Error += new ErrorEventHandler(OnWatcherError);

                // Parse "events" argument
                WatcherChangeTypes changes = 0;
                if (_args.Events.Contains("create"))
                {
                    changes |= WatcherChangeTypes.Created;
                    w.Created += new FileSystemEventHandler(OnWatcherNotification);
                }
                if (_args.Events.Contains("modify"))
                {
                    changes |= WatcherChangeTypes.Changed;
                    w.Changed += new FileSystemEventHandler(OnWatcherNotification);
                }
                if (_args.Events.Contains("delete"))
                {
                    changes |= WatcherChangeTypes.Deleted;
                    w.Deleted += new FileSystemEventHandler(OnWatcherNotification);
                }
                if (_args.Events.Contains("move"))
                {
                    changes |= WatcherChangeTypes.Renamed;
                    w.Renamed += new RenamedEventHandler(OnWatcherNotification);
                }

                // Main loop
                if (!_args.Quiet)
                {
                    Console.Error.WriteLine(
                        "===> {0} {1}{2}{3} for {4}",
                        _args.Monitor ? "Monitoring" : "Watching",
                        path,
                        Path.DirectorySeparatorChar,
                        fileName,
                        String.Join(", ", _args.Events)
                    );
                }
                w.EnableRaisingEvents = true;
                _stopMonitoringEvent.Wait();
            }
        }

        public void StdInOpen()
        {
            while (Console.ReadLine() != null);
            _stopMonitoring = true;
            _stopMonitoringEvent.Set();
        }

        /// Entry point
        public int Run()
        {
            using (_stopMonitoringEvent = new ManualResetEventSlim(initialState: false))
            {
                foreach (var path in _args.Paths)
                {
                    var t = new Thread(new ParameterizedThreadStart(Processor));
                    t.Start(path);
                    _threads.Add(t);
                }

                var stdInOpen = new Thread(new ThreadStart(StdInOpen));
                stdInOpen.IsBackground = true;
                stdInOpen.Start();

                _stopMonitoringEvent.Wait();

                foreach (var thread in _threads)
                {
                    if (thread.IsAlive) thread.Abort();
                    thread.Join();
                }
                return 0;
            }
        }

        /// Entry point method
        public static int Main(string[] args)
        {
            var p = new ArgumentParser();

            // Show usage if no args or standard "help" args are given
            if (0 == args.Length || args[0].Equals("-?") || args[0].Equals("--help"))
            {
                p.PrintUsage("inotifywait", Console.Error);
                return 1;
            }

            // Run!
            return new Runner(p.Parse(args)).Run();
        }
    }
}
