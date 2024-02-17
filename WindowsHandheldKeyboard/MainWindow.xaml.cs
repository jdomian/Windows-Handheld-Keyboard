using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WindowsInput;
using WindowsInput.Native;
using System.Windows.Controls.Primitives;
using System.Diagnostics;

namespace WindowsHandheldKeyboard
{
    public partial class MainWindow : Window
    {
        private InputSimulator inputSimulator;
        private WindowsInputDeviceStateAdaptor inputStateAdapter;
        private HwndSource hwndSource;
        private System.Windows.Forms.NotifyIcon notifyIcon = null;

        public MainWindow()
        {
            InitializeComponent();
            inputSimulator = new InputSimulator();
            inputStateAdapter = new WindowsInputDeviceStateAdaptor();
        }

        private void Window_Loaded(object sender, RoutedEventArgs rea)
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            if (desktopWorkingArea.Width > 0)
            {
                var Height = Math.Min(this.Height / this.Width * desktopWorkingArea.Width, desktopWorkingArea.Height);
                this.Width = desktopWorkingArea.Width;
                this.Height = Height;
                this.Left = 0;
                this.Top = desktopWorkingArea.Height * 0.15; //15% from top
            }
            else
            {
                this.Left = (desktopWorkingArea.Right - this.Width) / 2;
                this.Top = desktopWorkingArea.Bottom - this.Height;
            }

            var helper = new WindowInteropHelper(this);
            PInvokeWrapper.SetWindowNoFocus(helper.Handle);
            hwndSource = HwndSource.FromHwnd(helper.Handle);

            //Kills explorer.exe and restarts it to free up Win+Ctrl+Shift+O
            System.Diagnostics.Process.Start("taskkill", "/f /im explorer.exe");
            System.Threading.Thread.Sleep(1000);

            //Win+Ctrl+O is taken by explorer.exe, but we can workaround by killing explorer.exe and registering before restarting it
            PInvokeWrapper.TryRegisterHotKey(hwndSource, 9000, PInvokeWrapper.ModifierCode.MOD_WIN | PInvokeWrapper.ModifierCode.MOD_CONTROL, VirtualKeyCode.VK_O, () =>
            {
                this.Visibility = this.IsVisible ? Visibility.Hidden : Visibility.Visible;
            });

            //Restarts explorer.exe
            System.Diagnostics.Process.Start("explorer.exe");

            PInvokeWrapper.TryRegisterHotKey(hwndSource, 9001, PInvokeWrapper.ModifierCode.MOD_WIN | PInvokeWrapper.ModifierCode.MOD_SHIFT, VirtualKeyCode.VK_O, () =>
            {
                this.Visibility = this.IsVisible ? Visibility.Hidden : Visibility.Visible;
            });

            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Click += (s, e) =>
            {
                // Check if the left mouse button is clicked
                if (e is System.Windows.Forms.MouseEventArgs mouseEventArgs && mouseEventArgs.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Visibility = this.IsVisible ? Visibility.Hidden : Visibility.Visible;
                }
            };
            notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);

            // Add context menu
            System.Windows.Forms.ContextMenuStrip contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            // Adds menu item for showing/hiding the keyboard
            System.Windows.Forms.ToolStripMenuItem menuItem = new System.Windows.Forms.ToolStripMenuItem("Show/Hide (Win+Ctrl+O)");
            menuItem.Click += (menuItemSender, clickEventArgs) =>
            {
                this.Visibility = this.IsVisible ? Visibility.Hidden : Visibility.Visible;
            };
            contextMenu.Items.Add(menuItem);

            // Add menu item for increasing keyboard position upwards
            System.Windows.Forms.ToolStripMenuItem increasePositionUpMenuItem = new System.Windows.Forms.ToolStripMenuItem("Increase Position Upwards");
            increasePositionUpMenuItem.Click += (menuItemSender, clickEventArgs) =>
            {
                // Increase keyboard position upwards by 10% of the screen height
                double screenHeight = SystemParameters.WorkArea.Height;
                double increment = 0.1 * screenHeight;
                this.Top -= increment;
            };

            // Add menu item for decreasing keyboard position downwards
            System.Windows.Forms.ToolStripMenuItem decreasePositionDownMenuItem = new System.Windows.Forms.ToolStripMenuItem("Decrease Position Downwards");
            decreasePositionDownMenuItem.Click += (menuItemSender, clickEventArgs) =>
            {
                // Decrease keyboard position downwards by 10% of the screen height
                double screenHeight = SystemParameters.WorkArea.Height;
                double increment = 0.1 * screenHeight;
                this.Top += increment;
            };

            // Add "Quit Keyboard" menu item
            System.Windows.Forms.ToolStripMenuItem quitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Quit Keyboard");
            quitMenuItem.Click += (menuItemSender, clickEventArgs) =>
            {
                // Close the keyboard window and exit the application
                this.Close();
                System.Windows.Application.Current.Shutdown();
            };

            // Add a separator line
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            // Add "Exit" menu item
            System.Windows.Forms.ToolStripMenuItem exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitMenuItem.Click += (menuItemSender, clickEventArgs) =>
            {
                // Exit the application
                System.Windows.Application.Current.Shutdown();
            };

            // Add the menu items to the context menu
            
            
            contextMenu.Items.Add(increasePositionUpMenuItem);
            contextMenu.Items.Add(decreasePositionDownMenuItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(quitMenuItem);
            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            contextMenu.Items.Add(exitMenuItem);

            notifyIcon.ContextMenuStrip = contextMenu;

            notifyIcon.Visible = true;

            var modifierKeys = new HashSet<VirtualKeyCode> { VirtualKeyCode.LCONTROL, VirtualKeyCode.RCONTROL, VirtualKeyCode.LSHIFT, VirtualKeyCode.RSHIFT, VirtualKeyCode.LMENU, VirtualKeyCode.RMENU, VirtualKeyCode.LWIN, VirtualKeyCode.RWIN };
            var lockKeys = new HashSet<VirtualKeyCode> { VirtualKeyCode.CAPITAL, VirtualKeyCode.NUMLOCK, VirtualKeyCode.SCROLL, VirtualKeyCode.VOLUME_MUTE };

            foreach (var b in FindLogicalChildren<ToggleButton>(this))
            {
                if ((b.Tag is String s) && (s != ""))
                {
                    var keyCode = (VirtualKeyCode)Enum.Parse(typeof(VirtualKeyCode), s);
                    var isModifier = modifierKeys.Contains(keyCode);
                    var isLock = lockKeys.Contains(keyCode);

                    Action keyDown;
                    Action keyUp;

                    if (isLock)
                    {
                        Func<bool> getToggling = () =>
                        {
                            if (keyCode == VirtualKeyCode.VOLUME_MUTE)
                            {
                                return AudioManager.GetMasterVolumeMute();
                            }
                            else
                            {
                                return inputStateAdapter.IsTogglingKeyInEffect(keyCode);
                            }
                        };
                        var isChecked = getToggling();
                        b.IsChecked = isChecked;

                        keyDown = () =>
                        {
                            inputSimulator.Keyboard.KeyDown(keyCode);
                            isChecked = getToggling();
                            b.IsChecked = isChecked;
                        };
                        keyUp = () =>
                        {
                            inputSimulator.Keyboard.KeyUp(keyCode);
                            isChecked = getToggling();
                            b.IsChecked = isChecked;
                        };
                    }
                    else
                    {
                        keyDown = () =>
                        {
                            inputSimulator.Keyboard.KeyDown(keyCode);
                            b.IsChecked = true;
                        };
                        keyUp = () =>
                        {
                            inputSimulator.Keyboard.KeyUp(keyCode);
                            b.IsChecked = false;
                        };
                    }

                    var isPressing = false;
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Tick += (_, e) =>
                    {
                        timer.Stop();
                        keyDown();
                        timer.Interval = TimeSpan.FromMilliseconds(50);
                        timer.Start();
                    };
                    b.PreviewMouseDown += (_, e) =>
                    {
                        if (e.StylusDevice != null)
                        {
                            e.Handled = true;
                            return;
                        }
                        isPressing = true;
                        keyDown();
                        e.Handled = true;
                        if (!(isModifier || isLock))
                        {
                            timer.Interval = TimeSpan.FromMilliseconds(200);
                            timer.Start();
                        }
                    };
                    b.PreviewMouseUp += (_, e) =>
                    {
                        if (e.StylusDevice != null)
                        {
                            e.Handled = true;
                            return;
                        }
                        timer.Stop();
                        if (isPressing)
                        {
                            keyUp();
                            isPressing = false;
                            e.Handled = true;
                        }
                    };
                    b.MouseLeave += (_, e) =>
                    {
                        if (e.StylusDevice != null)
                        {
                            e.Handled = true;
                            return;
                        }
                        timer.Stop();
                        if (isPressing)
                        {
                            keyUp();
                            isPressing = false;
                            e.Handled = true;
                        }
                    };
                    b.PreviewTouchDown += (_, e) =>
                    {
                        isPressing = true;
                        keyDown();
                        e.Handled = true;
                        if (!(isModifier || isLock))
                        {
                            timer.Interval = TimeSpan.FromMilliseconds(200);
                            timer.Start();
                        }
                    };
                    b.PreviewTouchUp += (_, e) =>
                    {
                        timer.Stop();
                        if (isPressing)
                        {
                            keyUp();
                            isPressing = false;
                            e.Handled = true;
                        }
                    };
                    b.TouchLeave += (_, e) =>
                    {
                        timer.Stop();
                        if (isPressing)
                        {
                            keyUp();
                            isPressing = false;
                            e.Handled = true;
                        }
                    };
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            hwndSource.Dispose();
        }

        private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj != null)
            {
                if (obj is T)
                {
                    yield return obj as T;
                }

                foreach (DependencyObject child in LogicalTreeHelper.GetChildren(obj).OfType<DependencyObject>())
                {
                    foreach (T c in FindLogicalChildren<T>(child))
                    {
                        yield return c;
                    }
                }
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            // Start the process to terminate explorer.exe
            Process process = Process.Start("taskkill", "/f /im explorer.exe");
            if (process != null)
            {
                // Wait for the process to exit
                process.WaitForExit();

                // Start explorer.exe again
                Process.Start("explorer.exe");

                // Close the current application
                this.Close();
            }
        }

        private void ButtonHide_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Hidden;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                this.DragMove();
            }
        }
    }
}
