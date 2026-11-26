using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioSwitch.Platform;

namespace AudioSwitch.Host
{
    /// <summary>
    /// Ensures a single running instance. A second launch asks the running instance to
    /// open its flyout instead of creating a duplicate tray icon.
    ///
    /// Two signalling mechanisms are used:
    ///  * a hidden message-only window (works across integrity levels when the primary
    ///    process explicitly allows the message through UIPI), and
    ///  * a named auto-reset event as a fallback for same-integrity launches.
    ///
    /// This fixes the classic bug where a normal second launch cannot signal an elevated
    /// (high integrity) primary instance: the named event created by the elevated process
    /// cannot be written by a lower integrity process without an explicit UIPI filter.
    /// </summary>
    internal sealed class SingleInstanceGuard : IDisposable
    {
        private const string MutexName = @"Local\AudioSwitch.SingleInstance";
        private const string ShowEventName = @"Local\AudioSwitch.ShowFlyout";
        private const string MessageWindowTitle = "AudioSwitch.SingleInstance";
        private const int ShowMessage = NativeMethods.WM_APP + 2;
        private static readonly IntPtr MessageOnlyParent = new IntPtr(-3); // HWND_MESSAGE

        private readonly Mutex _mutex;
        private readonly bool _ownsMutex;
        private readonly Dispatcher _dispatcher;
        private readonly string _showEventName;
        private readonly string _messageWindowTitle;

        private HwndSource _messageWindow;
        private EventWaitHandle _showEvent;
        private Thread _listenerThread;
        private volatile bool _stopping;
        private int _pendingShowRequest;
        private bool _disposed;

        private SingleInstanceGuard(Mutex mutex, bool ownsMutex, Dispatcher dispatcher,
            string mutexName, string showEventName, string messageWindowTitle)
        {
            _mutex = mutex;
            _ownsMutex = ownsMutex;
            _dispatcher = dispatcher;
            _showEventName = showEventName;
            _messageWindowTitle = messageWindowTitle;
        }

        public bool IsPrimary { get; private set; }

        public event EventHandler ShowRequested;

        public static SingleInstanceGuard TryAcquire(Dispatcher dispatcher)
        {
            return TryAcquire(dispatcher, MutexName, ShowEventName, MessageWindowTitle);
        }

        /// <summary>
        /// Test/advanced overload that allows isolated named objects (the unit tests use
        /// unique names so they cannot disturb a real running instance).
        /// </summary>
        internal static SingleInstanceGuard TryAcquire(Dispatcher dispatcher, string mutexName,
            string showEventName, string messageWindowTitle)
        {
            if (string.IsNullOrEmpty(mutexName) || string.IsNullOrEmpty(showEventName)
                || string.IsNullOrEmpty(messageWindowTitle))
            {
                throw new ArgumentNullException("mutexName");
            }

            Mutex mutex = null;
            bool createdNew = false;
            bool owns = false;
            bool cannotOpenExisting = false;

            try
            {
                mutex = new Mutex(false, mutexName, out createdNew, CreateMutexSecurity());
            }
            catch (UnauthorizedAccessException)
            {
                // Another instance (typically elevated) created the mutex with an older,
                // restrictive ACL. We cannot open it, but it means an instance exists.
                cannotOpenExisting = true;
            }
            catch (Exception)
            {
                // Named mutex creation is a convenience; never prevent the app from running.
            }

            if (mutex != null)
            {
                if (createdNew)
                {
                    try
                    {
                        owns = mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        owns = true;
                    }
                }
                else
                {
                    try
                    {
                        owns = mutex.WaitOne(0);
                    }
                    catch (AbandonedMutexException)
                    {
                        owns = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        owns = false;
                    }
                }
            }
            else if (!cannotOpenExisting)
            {
                // Could not create any named object at all; still run as a normal instance.
                owns = true;
            }

            SingleInstanceGuard guard = new SingleInstanceGuard(mutex, owns, dispatcher,
                mutexName, showEventName, messageWindowTitle);
            guard.IsPrimary = owns;

            if (owns)
            {
                guard.StartListener();
            }

            return guard;
        }

        /// <summary>
        /// Asks the already running instance to show its flyout. Returns true when the
        /// request was delivered (or at least posted).
        /// </summary>
        public bool SignalPrimary()
        {
            // The primary instance creates its message window inside TryAcquire, but a
            // second launch may arrive while the primary is still starting up. Retry the
            // window lookup briefly instead of immediately falling back/failing.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                IntPtr window = IntPtr.Zero;
                try
                {
                    window = NativeMethods.FindWindowEx(MessageOnlyParent, IntPtr.Zero, null,
                        _messageWindowTitle);
                }
                catch
                {
                    window = IntPtr.Zero;
                }

                if (window != IntPtr.Zero)
                {
                    try
                    {
                        if (NativeMethods.PostMessage(window, ShowMessage, IntPtr.Zero, IntPtr.Zero))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Fall through to the named event.
                    }
                }

                Thread.Sleep(150);
            }

            return SignalPrimaryViaEvent();
        }

        private bool SignalPrimaryViaEvent()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using (EventWaitHandle existing = EventWaitHandle.OpenExisting(_showEventName,
                        EventWaitHandleRights.Modify))
                    {
                        existing.Set();
                        return true;
                    }
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private void StartListener()
        {
            StartMessageWindow();
            StartEventListener();
        }

        private void StartMessageWindow()
        {
            try
            {
                HwndSourceParameters parameters = new HwndSourceParameters(_messageWindowTitle)
                {
                    ParentWindow = MessageOnlyParent,
                    WindowStyle = 0,
                    Width = 0,
                    Height = 0
                };

                _messageWindow = new HwndSource(parameters);
                _messageWindow.AddHook(OnMessageWindowHook);

                // Lower integrity processes are blocked from posting to higher integrity
                // windows by UIPI unless the receiving window explicitly allows the message.
                NativeMethods.ChangeWindowMessageFilterEx(_messageWindow.Handle, ShowMessage,
                    NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
            }
            catch
            {
                // Without the message window, same-integrity event signalling still works.
                _messageWindow = null;
            }
        }

        private IntPtr OnMessageWindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
            ref bool handled)
        {
            if (msg != ShowMessage)
            {
                return IntPtr.Zero;
            }

            handled = true;
            RaiseShowRequested();
            return IntPtr.Zero;
        }

        private void StartEventListener()
        {
            try
            {
                bool createdNew;
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _showEventName,
                    out createdNew, CreateEventSecurity());
            }
            catch (UnauthorizedAccessException)
            {
                try
                {
                    _showEvent = EventWaitHandle.OpenExisting(_showEventName, EventWaitHandleRights.Modify);
                }
                catch
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            _listenerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "AudioSwitch.ShowFlyoutListener"
            };
            _listenerThread.Start();
        }

        private void ListenLoop()
        {
            while (!_stopping)
            {
                try
                {
                    if (!_showEvent.WaitOne(500))
                    {
                        continue;
                    }

                    if (_stopping)
                    {
                        return;
                    }

                    RaiseShowRequested();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    return;
                }
            }
        }

        private void RaiseShowRequested()
        {
            // Buffer the request: the message window exists before App subscribes to
            // ShowRequested, so a very early PostMessage must not be lost.
            Interlocked.Exchange(ref _pendingShowRequest, 1);

            _dispatcher.BeginInvoke(new Action(delegate
            {
                if (_stopping)
                {
                    return;
                }

                EventHandler handler = ShowRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }));
        }

        /// <summary>
        /// Returns true when a show request arrived before it could be delivered to a
        /// subscriber (startup race) and clears the pending flag.
        /// </summary>
        internal bool ConsumePendingShowRequest()
        {
            return Interlocked.Exchange(ref _pendingShowRequest, 0) == 1;
        }

        private static MutexSecurity CreateMutexSecurity()
        {
            MutexSecurity security = new MutexSecurity();
            SecurityIdentifier authenticated =
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new MutexAccessRule(authenticated,
                MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User,
                MutexRights.FullControl, AccessControlType.Allow));
            return security;
        }

        private static EventWaitHandleSecurity CreateEventSecurity()
        {
            EventWaitHandleSecurity security = new EventWaitHandleSecurity();
            SecurityIdentifier authenticated =
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new EventWaitHandleAccessRule(authenticated,
                EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify,
                AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(WindowsIdentity.GetCurrent().User,
                EventWaitHandleRights.FullControl, AccessControlType.Allow));
            return security;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;

            if (_listenerThread != null)
            {
                try
                {
                    _listenerThread.Join(200);
                }
                catch
                {
                    // Ignore.
                }

                _listenerThread = null;
            }

            if (_showEvent != null)
            {
                try
                {
                    _showEvent.Set();
                }
                catch
                {
                    // Ignore.
                }

                try
                {
                    _showEvent.Dispose();
                }
                catch
                {
                    // Ignore.
                }

                _showEvent = null;
            }

            if (_messageWindow != null)
            {
                try
                {
                    _messageWindow.RemoveHook(OnMessageWindowHook);
                    _messageWindow.Dispose();
                }
                catch
                {
                    // Ignore.
                }

                _messageWindow = null;
            }

            if (_ownsMutex && _mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore.
                }
            }

            if (_mutex != null)
            {
                try
                {
                    _mutex.Dispose();
                }
                catch
                {
                    // Ignore.
                }
            }
        }
    }
}
