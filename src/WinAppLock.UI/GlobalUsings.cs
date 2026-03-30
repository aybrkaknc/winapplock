// ═══════════════════════════════════════════════════
// WinAppLock.UI — Global Using Yönlendirmeleri
// WPF ve Windows Forms birlikte kullanıldığında oluşan
// namespace çakışmalarını çözer.
// ═══════════════════════════════════════════════════

// Application, Window, UI temel tipleri
global using Application = System.Windows.Application;
global using Window = System.Windows.Window;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;

// Controls
global using UserControl = System.Windows.Controls.UserControl;
global using Button = System.Windows.Controls.Button;
global using TextBox = System.Windows.Controls.TextBox;
global using ComboBox = System.Windows.Controls.ComboBox;

// Input & Events
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;

// Drag & Drop
global using DragEventArgs = System.Windows.DragEventArgs;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DataFormats = System.Windows.DataFormats;

// Media
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using FontFamily = System.Windows.Media.FontFamily;

// Diğer
global using Clipboard = System.Windows.Clipboard;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

// WinAppLock Servisleri
global using LocalizationManager = WinAppLock.UI.Services.LocalizationManager;

