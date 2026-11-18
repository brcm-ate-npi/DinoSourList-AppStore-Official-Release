// Verify that the packaged adapter returns a real CoreVideo link that accepts a callback and starts.
#include <CoreVideo/CoreVideo.h>
#include <dlfcn.h>
#include <stdio.h>

typedef CVReturn (*CreateDisplayLink)(CVDisplayLinkRef *);
static CVReturn tick(CVDisplayLinkRef link, const CVTimeStamp *now, const CVTimeStamp *output,
                     CVOptionFlags input_flags, CVOptionFlags *output_flags, void *context)
{
    return kCVReturnSuccess;
}
int main(int argc, char **argv)
{
    if (argc != 2) return 1;
    void *adapter = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL | RTLD_FIRST);
    if (adapter == NULL) { fprintf(stderr, "%s\n", dlerror()); return 1; }
    CreateDisplayLink create = (CreateDisplayLink)dlsym(adapter, "CVDisplayLinkCreateWithActiveCGDisplays");
    if (create == NULL) return 1;
    CVDisplayLinkRef link = NULL;
    CVReturn result = create(&link);
    printf("Adapter display-link result: %d\n", result);
    if (result != kCVReturnSuccess || link == NULL) return 1;
    result = CVDisplayLinkSetOutputCallback(link, tick, NULL);
    if (result == kCVReturnSuccess) result = CVDisplayLinkStart(link);
    printf("Real callback/start result: %d\n", result);
    if (CVDisplayLinkIsRunning(link)) CVDisplayLinkStop(link);
    CVDisplayLinkRelease(link);
    dlclose(adapter);
    return result == kCVReturnSuccess ? 0 : 1;
}
