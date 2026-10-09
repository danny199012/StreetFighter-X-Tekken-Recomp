// SFxT - ReXGlue Recompiled Project

#include "generated/default/SFxT_init.h"

#include "SFxT_app.h"

// ---------------------------------------------------------------------------
// DPI awareness. Without this the process is DPI-unaware: on scaled displays
// Windows virtualizes all coordinates (a 4K screen at 225% looks like a
// 1707x960 screen to us), so requested window sizes come out wrong and the
// image appears zoomed. Run before any window is created.
// ---------------------------------------------------------------------------
#ifndef DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
#define DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 ((DPI_AWARENESS_CONTEXT)-4)
#endif

namespace {
struct DpiAwareInit {
	DpiAwareInit()
	{
		using SetProcessDpiAwarenessContextFn =
			int(__stdcall*)(void*);
		const auto setContext = reinterpret_cast<SetProcessDpiAwarenessContextFn>(
			GetProcAddress(GetModuleHandleW(L"user32.dll"),
				"SetProcessDpiAwarenessContext"));
		if (setContext == nullptr ||
			!setContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
		{
			// pre-1703 Windows fallback
			SetProcessDPIAware();
		}
	}
};
const DpiAwareInit dpi_aware_init;
} // namespace

REX_DEFINE_APP(SFxT, SfxtApp::Create)
