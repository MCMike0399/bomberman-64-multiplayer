// CI-only private display API declarations, corroborated by Chromium's
// ui/display/mac/test/virtual_display_util_mac.mm. No display helper ships in the game.
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <signal.h>

@interface BCDisplayDescriptor : NSObject
@property unsigned int vendorID, productID, serialNum, serialNumber;
@property unsigned int maxPixelsWide, maxPixelsHigh;
@property CGSize sizeInMillimeters;
@property CGPoint redPrimary, greenPrimary, bluePrimary, whitePoint;
@property(strong) NSString *name;
@property(strong) id queue;
@end
@interface BCDisplayMode : NSObject
- (id)initWithWidth:(unsigned int)width height:(unsigned int)height refreshRate:(double)rate;
@end
@interface BCDisplaySettings : NSObject
@property unsigned int hiDPI, rotation;
@property(strong) NSArray *modes;
@end
@interface BCDisplay : NSObject
- (id)initWithDescriptor:(id)descriptor;
- (BOOL)applySettings:(id)settings;
@property(readonly) unsigned int displayID;
@end

static volatile sig_atomic_t stopping = 0;
static void stopDisplay(int signal) { stopping = 1; }

int main(int argc, const char **argv) {
    @autoreleasepool {
        if (argc != 2 || ![NSProcessInfo.processInfo.environment[@"GITHUB_ACTIONS"] isEqualToString:@"true"]) return 2;
        signal(SIGTERM, stopDisplay); signal(SIGINT, stopDisplay);
        CGDirectDisplayID original = CGMainDisplayID(), selected = original;
        __strong BCDisplay *virtualDisplay = nil;
        NSArray *modes = CFBridgingRelease(CGDisplayCopyAllDisplayModes(original,
            (__bridge CFDictionaryRef)@{(__bridge NSString *)kCGDisplayShowDuplicateLowResolutionModes: @YES}));
        CGDisplayModeRef best = NULL;
        for (id object in modes) {
            CGDisplayModeRef mode = (__bridge CGDisplayModeRef)object;
            if (CGDisplayModeGetPixelWidth(mode) >= 1920 && CGDisplayModeGetPixelHeight(mode) >= 1200 &&
                (!best || CGDisplayModeGetPixelWidth(mode) * CGDisplayModeGetPixelHeight(mode) < CGDisplayModeGetPixelWidth(best) * CGDisplayModeGetPixelHeight(best))) best = mode;
        }
        if (best) {
            if (CGDisplaySetDisplayMode(original, best, NULL) != kCGErrorSuccess) return 3;
        } else {
            BCDisplayDescriptor *descriptor = [[NSClassFromString(@"CGVirtualDisplayDescriptor") alloc] init];
            if (!descriptor) { fprintf(stderr, "Virtual display API unavailable\n"); return 4; }
            descriptor.name = @"Blast Circuit owned CI display";
            descriptor.vendorID = 0x424c; descriptor.productID = 1;
            descriptor.serialNum = (unsigned int)getpid(); descriptor.serialNumber = descriptor.serialNum;
            descriptor.maxPixelsWide = 1920; descriptor.maxPixelsHigh = 1200;
            descriptor.sizeInMillimeters = CGSizeMake(508, 317.5);
            descriptor.redPrimary = CGPointMake(.64, .33); descriptor.greenPrimary = CGPointMake(.30, .60);
            descriptor.bluePrimary = CGPointMake(.15, .06); descriptor.whitePoint = CGPointMake(.3127, .3290);
            descriptor.queue = dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_HIGH, 0);
            virtualDisplay = [[NSClassFromString(@"CGVirtualDisplay") alloc] initWithDescriptor:descriptor];
            if (!virtualDisplay) { fprintf(stderr, "Could not create owned virtual display\n"); return 5; }
            BCDisplaySettings *settings = [[NSClassFromString(@"CGVirtualDisplaySettings") alloc] init];
            settings.hiDPI = 0; settings.rotation = 0;
            BCDisplayMode *mode = [[NSClassFromString(@"CGVirtualDisplayMode") alloc] initWithWidth:1920 height:1200 refreshRate:60];
            settings.modes = @[mode];
            if (![virtualDisplay applySettings:settings]) return 6;
            selected = virtualDisplay.displayID;
            CGDisplayConfigRef configuration = NULL;
            if (CGBeginDisplayConfiguration(&configuration) != kCGErrorSuccess) return 7;
            CGConfigureDisplayOrigin(configuration, original, 1920, 0);
            CGConfigureDisplayOrigin(configuration, selected, 0, 0);
            if (CGCompleteDisplayConfiguration(configuration, kCGConfigureForSession) != kCGErrorSuccess) return 8;
        }
        for (int attempt = 0; attempt < 50 && (CGMainDisplayID() != selected || CGDisplayPixelsWide(selected) < 1920 || CGDisplayPixelsHigh(selected) < 1200); attempt++)
            [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.1]];
        if (CGMainDisplayID() != selected || CGDisplayPixelsWide(selected) < 1920 || CGDisplayPixelsHigh(selected) < 1200) return 9;
        NSDictionary *report = @{@"displayId": @(selected), @"width": @(CGDisplayPixelsWide(selected)),
            @"height": @(CGDisplayPixelsHigh(selected)), @"virtual": @(virtualDisplay != nil)};
        NSData *data = [NSJSONSerialization dataWithJSONObject:report options:NSJSONWritingPrettyPrinted error:nil];
        if (![data writeToFile:[NSString stringWithUTF8String:argv[1]] atomically:YES]) return 10;
        while (!stopping) [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.2]];
        if (virtualDisplay) {
            CGDisplayConfigRef configuration = NULL;
            if (CGBeginDisplayConfiguration(&configuration) == kCGErrorSuccess) {
                CGConfigureDisplayOrigin(configuration, selected, 1920, 0);
                CGConfigureDisplayOrigin(configuration, original, 0, 0);
                CGCompleteDisplayConfiguration(configuration, kCGConfigureForSession);
            }
        }
        virtualDisplay = nil;
    }
    return 0;
}
