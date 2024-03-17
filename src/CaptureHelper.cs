using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Graphics.Capture;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace AlexanderSklar.ScreenCapture
{
    public static class CaptureHelper
    {
        static readonly Guid GraphicsCaptureItemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        public static async Task<SoftwareBitmap> CaptureMonitor(nint monitorValue = -1)
        {
            if (monitorValue == -1)
            {
                monitorValue = MonitorFromWindow(HWND.Null, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            }
            var monitor = new HMONITOR(monitorValue);
            Windows.Graphics.Capture.GraphicsCaptureItem item = CaptureHelper.CreateItemForMonitor(monitor);
            if (item == null)
                throw new Exception("Failed to start monitor capture");

            return await CaptureItemAsync(item);
        }

        public static async Task<SoftwareBitmap> CaptureWindow(nint hwndValue)
        {
            var hwnd = new HWND(hwndValue);
            Windows.Graphics.Capture.GraphicsCaptureItem item = CaptureHelper.CreateItemForWindow(hwnd);
            if (item == null)
                throw new Exception("Failed to start window capture");

            return await CaptureItemAsync(item);
        }

        private static IDirect3DDevice _canvasDevice;

        private static IDirect3DDevice CanvasDevice
        {
            get
            {
                if (_canvasDevice == null)
                {
                    _canvasDevice = Direct3D11Helper.CreateDevice();
                }
                return _canvasDevice;
            }
        }

        private static async Task<SoftwareBitmap> CaptureItemAsync(GraphicsCaptureItem item)
        {
            var _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                CanvasDevice,
                Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);

            var session = _framePool.CreateCaptureSession(item);
            session.IsBorderRequired = false;
            _frameProcessor = new FrameProcessor() { Session = session };

            _framePool.FrameArrived += _frameProcessor.FrameArrived;
            session.StartCapture();
            return await _frameProcessor.Task;
        }

        private static FrameProcessor _frameProcessor;

        class FrameProcessor
        {
            private TaskCompletionSource<SoftwareBitmap> _tcs = new();
            public Task<SoftwareBitmap> Task => _tcs.Task;
            public GraphicsCaptureSession Session { get; set; }


            internal async void FrameArrived(Direct3D11CaptureFramePool sender, object e)
            {
                try
                {
                    using (var frame = sender.TryGetNextFrame())
                    {
                        if (frame is null) return;
                        Session.Dispose();
                        var sbitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
                        _tcs.SetResult(sbitmap);
                    }
                }
                catch (Exception ex)
                {
                    _tcs.SetException(ex);
                }
            }

        }


        internal static GraphicsCaptureItem CreateItemForWindow(HWND hwnd)
        {
            GraphicsCaptureItem item = null;
            unsafe
            {
                item = CreateItemForCallback((IGraphicsCaptureItemInterop interop, Guid* guid) =>
                {
                    interop.CreateForWindow(hwnd, guid, out object raw);
                    return raw;
                });
            }
            return item;
        }

        internal static GraphicsCaptureItem CreateItemForMonitor(HMONITOR hmon)
        {
            GraphicsCaptureItem item = null;
            unsafe
            {
                item = CreateItemForCallback((IGraphicsCaptureItemInterop interop, Guid* guid) =>
                {
                    interop.CreateForMonitor(hmon, guid, out object raw);
                    return raw;
                });
            }
            return item;
        }

        private unsafe delegate object InteropCallback(IGraphicsCaptureItemInterop interop, Guid* guid);

        private static GraphicsCaptureItem CreateItemForCallback(InteropCallback callback)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            GraphicsCaptureItem item = null;
            unsafe
            {
                var guid = GraphicsCaptureItemGuid;
                var guidPointer = (Guid*)Unsafe.AsPointer(ref guid);
                var raw = Marshal.GetIUnknownForObject(callback(interop, guidPointer));
                item = GraphicsCaptureItem.FromAbi(raw);
                Marshal.Release(raw);
            }
            return item;
        }
    }
}
