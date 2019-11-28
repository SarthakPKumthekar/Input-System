#include "iOSScreenKeyboardDelegate.h"
#include "DisplayManager.h"
#include "UnityAppController.h"
#include "UnityForwardDecls.h"
#include <string>

// Keep in sync with ScreenKeyboardType from com.unity.inputsystem/InputSystem/Devices/ScreenKeyboard.cs
enum iOSScreenKeyboardType : int
{
    kDefault = 0,
    kASCIICapable = 1,
    kNumbersAndPunctuation = 2,
    kURL = 3,
    kNumberPad = 4,
    kPhonePad = 5,
    kNamePhonePad = 6,
    kEmailAddress = 7,
    kSocial = 8,
    kSearch = 9
};

struct iOSScreenKeyboardShowParams
{
    iOSScreenKeyboardType type;
    const char* initialText;
    const char* placeholderText;
    int autocorrection;
    int multiline;
    int secure;
    int alert;
    int inputFieldHidden;
};

struct UnityRect
{
    float x;
    float y;
    float width;
    float height;
};

UIKeyboardType GetUIKeyboardType(iOSScreenKeyboardType type)
{
    switch (type)
    {
        case kDefault:
            return UIKeyboardTypeDefault;
        case kASCIICapable:
            return UIKeyboardTypeASCIICapable;
        case kNumbersAndPunctuation:
            return UIKeyboardTypeNumbersAndPunctuation;
        case kURL:
            return UIKeyboardTypeURL;
        case kNumberPad:
            return UIKeyboardTypeNumberPad;
        case kPhonePad:
            return UIKeyboardTypePhonePad;
        case kNamePhonePad:
            return UIKeyboardTypeNamePhonePad;
        case kEmailAddress:
            return UIKeyboardTypeEmailAddress;
        case kSocial:
            return UIKeyboardTypeTwitter;
        case kSearch:
            return UIKeyboardTypeWebSearch;
        default:
            NSLog(@"Unknown keyboard type: %d", type);
            return UIKeyboardTypeDefault;
    }
}

extern "C" void _iOSScreenKeyboardShow(iOSScreenKeyboardShowParams* showParams, int sizeOfShowParams, iOSScreenKeyboardCallbacks* callbacks, int sizeOfCallbacks)
{
    if (sizeof(iOSScreenKeyboardShowParams) != sizeOfShowParams)
    {
        NSLog(@"ScreenKeyboardShowParams size mismatch, expected %lu was %d", sizeof(iOSScreenKeyboardShowParams), sizeOfShowParams);
        return;
    }

    if (sizeof(iOSScreenKeyboardCallbacks) != sizeOfCallbacks)
    {
        NSLog(@"iOSScreenKeyboardCallbacks size mismatch, expected %lu was %d", sizeof(iOSScreenKeyboardCallbacks), sizeOfCallbacks);
        return;
    }

    iOSScreenKeyboardShowParamsNative param =
    {
        GetUIKeyboardType(showParams->type),
        showParams->autocorrection ? UITextAutocorrectionTypeDefault : UITextAutocorrectionTypeNo,
        showParams->alert ? UIKeyboardAppearanceAlert : UIKeyboardAppearanceDefault,
#if PLATFORM_TVOS
        //The API for showing keyboard for editing multi-line text is not available on tvOS
        FALSE,
#else
        (BOOL)showParams->multiline,
#endif
        (BOOL)showParams->secure,
        *callbacks
    };

    [[iOSScreenKeyboardDelegate getInstanceOrCreate] show:param withInitialTextCStr:showParams->initialText withPlaceholderTextCStr:showParams->placeholderText];
}

extern "C" UnityRect _iOSScreenKeyboardOccludingArea()
{
    iOSScreenKeyboardDelegate* keyboard = [iOSScreenKeyboardDelegate getInstance];
    if (keyboard == NULL)
    {
        UnityRect zero = {0, 0, 0, 0};
        return zero;
    }
    CGRect rc = keyboard.area;
    UnityRect unityRC = {(float)rc.origin.x, (float)rc.origin.y, (float)rc.size.width, (float)rc.size.height};
    return unityRC;
}

extern "C" void _iOSScreenKeyboardSetInputFieldText(const char* text)
{
    iOSScreenKeyboardDelegate* keyboard = [iOSScreenKeyboardDelegate getInstance];
    if (keyboard == NULL)
        return;
    NSString* convertedText = text ? [[NSString alloc] initWithUTF8String: text] : @"";
    [keyboard setText: convertedText];
}

extern "C" const char* _iOSScreenKeyboardGetInputFieldText()
{
    iOSScreenKeyboardDelegate* keyboard = [iOSScreenKeyboardDelegate getInstance];
    if (keyboard == NULL)
        return NULL;
    
    return strdup([[keyboard getText] UTF8String]);
}

