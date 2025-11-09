#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

extern "C" {
    // Light impact feedback
    void TapticEngine_ImpactLight() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        }
    }

    // Medium impact feedback
    void TapticEngine_ImpactMedium() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        }
    }

    // Heavy impact feedback
    void TapticEngine_ImpactHeavy() {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        }
    }

    // Rigid impact feedback (iOS 13+)
    void TapticEngine_ImpactRigid() {
        if (@available(iOS 13.0, *)) {
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        } else if (@available(iOS 10.0, *)) {
            // Fallback to heavy for older iOS versions
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        }
    }

    // Soft impact feedback (iOS 13+)
    void TapticEngine_ImpactSoft() {
        if (@available(iOS 13.0, *)) {
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        } else if (@available(iOS 10.0, *)) {
            // Fallback to light for older iOS versions
            UIImpactFeedbackGenerator *impactFeedback = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            [impactFeedback prepare];
            [impactFeedback impactOccurred];
        }
    }

    // Selection feedback
    void TapticEngine_Selection() {
        if (@available(iOS 10.0, *)) {
            UISelectionFeedbackGenerator *selectionFeedback = [[UISelectionFeedbackGenerator alloc] init];
            [selectionFeedback prepare];
            [selectionFeedback selectionChanged];
        }
    }

    // Notification feedback - Success
    void TapticEngine_NotificationSuccess() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *notificationFeedback = [[UINotificationFeedbackGenerator alloc] init];
            [notificationFeedback prepare];
            [notificationFeedback notificationOccurred:UINotificationFeedbackTypeSuccess];
        }
    }

    // Notification feedback - Warning
    void TapticEngine_NotificationWarning() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *notificationFeedback = [[UINotificationFeedbackGenerator alloc] init];
            [notificationFeedback prepare];
            [notificationFeedback notificationOccurred:UINotificationFeedbackTypeWarning];
        }
    }

    // Notification feedback - Error
    void TapticEngine_NotificationError() {
        if (@available(iOS 10.0, *)) {
            UINotificationFeedbackGenerator *notificationFeedback = [[UINotificationFeedbackGenerator alloc] init];
            [notificationFeedback prepare];
            [notificationFeedback notificationOccurred:UINotificationFeedbackTypeError];
        }
    }
}
