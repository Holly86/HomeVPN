global using System.IO;

// WPF and WinForms are both enabled because the notification-area icon uses
// System.Windows.Forms.NotifyIcon. The Windows Desktop SDK therefore makes
// several simple type names ambiguous. Keep the application itself WPF-first
// and use explicit aliases for the overlapping UI types.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Brush = System.Windows.Media.Brush;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using Button = System.Windows.Controls.Button;
