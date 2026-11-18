// macOS compatibility adapter for Avalonia.Native's legacy aggregate display-link call.
// Preserve Apple's normal result; only retry kCVReturnInvalidArgument on an online main display.
// The real CoreVideo framework is re-exported. No system framework is copied or modified.
#include <CoreVideo/CoreVideo.h>
#include <CoreGraphics/CoreGraphics.h>
#include <dlfcn.h>
#include <pthread.h>

typedef CVReturn (*CreateDisplayLink)(CVDisplayLinkRef *);
static CreateDisplayLink original_create;
static pthread_once_t initialize_once = PTHREAD_ONCE_INIT;

static void initialize_original(void)
{
    // Keep the handle for this adapter's process lifetime. RTLD_FIRST avoids resolving our export.
    void *framework = dlopen("/System/Library/Frameworks/CoreVideo.framework/CoreVideo",
                             RTLD_LAZY | RTLD_LOCAL | RTLD_FIRST);
    if (framework != NULL)
        original_create = (CreateDisplayLink)dlsym(framework, "CVDisplayLinkCreateWithActiveCGDisplays");
}

CVReturn CVDisplayLinkCreateWithActiveCGDisplays(CVDisplayLinkRef *display_link)
{
    if (display_link == NULL) return kCVReturnInvalidArgument;
    pthread_once(&initialize_once, initialize_original);
    if (original_create == NULL) return kCVReturnError;
    CVReturn result = original_create(display_link);
    if (result != kCVReturnInvalidArgument) return result;
    CGDirectDisplayID main_display = CGMainDisplayID();
    if (main_display == kCGNullDirectDisplay || !CGDisplayIsOnline(main_display)) return result;
    // A successful return still requires a real display link; callback/start errors remain visible.
    return CVDisplayLinkCreateWithCGDisplay(main_display, display_link);
}
