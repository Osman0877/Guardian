using System;
using System.Runtime.InteropServices;

namespace Guardian.Core.Win32
{
    public static class NativeTray
    {
        public const int NIM_ADD = 0x00000000;
        public const int NIM_MODIFY = 0x00000001;
        public const int NIM_DELETE = 0x00000002;

        public const int NIF_MESSAGE = 0x00000001;
        public const int NIF_ICON = 0x00000002;
        public const int NIF_TIP = 0x00000004;

        public const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 100;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONUP = 0x0205;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        public static readonly IntPtr IDI_APPLICATION = (IntPtr)32512;
        public static readonly IntPtr IDI_SHIELD = (IntPtr)32518;
    }

    public class GuardianTrayIcon : IDisposable
    {
        private NativeTray.NOTIFYICONDATA _nid;
        private bool _isAdded = false;

        public event Action? OnTrayClicked;

        public GuardianTrayIcon(string tip, IntPtr hWnd, IntPtr hIcon)
        {
            _nid = new NativeTray.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NativeTray.NOTIFYICONDATA>(),
                hWnd = hWnd,
                uID = 1001,
                uFlags = NativeTray.NIF_MESSAGE | NativeTray.NIF_ICON | NativeTray.NIF_TIP,
                uCallbackMessage = NativeTray.WM_TRAYICON,
                hIcon = hIcon != IntPtr.Zero ? hIcon : NativeTray.LoadIcon(IntPtr.Zero, NativeTray.IDI_SHIELD),
                szTip = tip
            };
        }

        public void Show()
        {
            if (!_isAdded && _nid.hWnd != IntPtr.Zero)
            {
                _isAdded = NativeTray.Shell_NotifyIcon(NativeTray.NIM_ADD, ref _nid);
            }
        }

        public void Hide()
        {
            if (_isAdded)
            {
                NativeTray.Shell_NotifyIcon(NativeTray.NIM_DELETE, ref _nid);
                _isAdded = false;
            }
        }

        public void Dispose()
        {
            Hide();
        }
    }
}
