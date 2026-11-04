// Haptics and the Reduce Motion preference for Farmverse (NativeHaptics.cs).
// Kinds match Farmverse.Application.Ports.HapticKind:
// 0 selection, 1 light, 2 medium, 3 heavy, 4 success, 5 warning, 6 error.
#import <UIKit/UIKit.h>

static UISelectionFeedbackGenerator *fvSelection;
static UIImpactFeedbackGenerator *fvLight;
static UIImpactFeedbackGenerator *fvMedium;
static UIImpactFeedbackGenerator *fvHeavy;
static UINotificationFeedbackGenerator *fvNotify;

static void FarmverseHapticsSetUp(void)
{
    if (fvSelection != nil) return;
    fvSelection = [[UISelectionFeedbackGenerator alloc] init];
    fvLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    fvMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
    fvHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
    fvNotify = [[UINotificationFeedbackGenerator alloc] init];
}

extern "C" void FarmverseHaptic(int kind)
{
    FarmverseHapticsSetUp();
    switch (kind)
    {
        case 0: [fvSelection selectionChanged]; [fvSelection prepare]; break;
        case 1: [fvLight impactOccurred]; [fvLight prepare]; break;
        case 2: [fvMedium impactOccurred]; [fvMedium prepare]; break;
        case 3: [fvHeavy impactOccurred]; [fvHeavy prepare]; break;
        case 4: [fvNotify notificationOccurred:UINotificationFeedbackTypeSuccess]; break;
        case 5: [fvNotify notificationOccurred:UINotificationFeedbackTypeWarning]; break;
        default: [fvNotify notificationOccurred:UINotificationFeedbackTypeError]; break;
    }
}

extern "C" bool FarmverseReduceMotion(void)
{
    return UIAccessibilityIsReduceMotionEnabled();
}
