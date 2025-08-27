// aiCam / ARObjCCamera.mm
// iOS (Objective-C++) bridge: Unity から呼び出す「ネイティブ静止画キャプチャ」
// と「合成済みPNGのフォトライブラリ保存」を提供します。
// ※公開C関数名は維持（ARNative_CaptureOneShot / ARNative_SavePNGToPhotos）
#import <AVFoundation/AVFoundation.h>
#import <Photos/Photos.h>
#import <UIKit/UIKit.h>

@interface _ARPhotoDelegate : NSObject <AVCapturePhotoCaptureDelegate>
@property(nonatomic,strong) AVCaptureSession *session;
@property(nonatomic,strong) AVCapturePhotoOutput *output;
@end

// デリゲートの寿命を確実に保つための静的保持（処理完了で nil に戻す）
static __strong _ARPhotoDelegate *_gPhotoDelegate = nil;

@implementation _ARPhotoDelegate

- (void)finish
{
    // セッション終了・解放（例外に依存しない安全停止）
    if (self.session) {
        @autoreleasepool {
            if (self.session.isRunning) {
                [self.session stopRunning];
            }
        }
    }
    self.session = nil;
    self.output  = nil;
    _gPhotoDelegate = nil;
}

- (void)captureOutput:(AVCapturePhotoOutput *)output
didFinishProcessingPhoto:(AVCapturePhoto *)photo
                error:(NSError *)error
{
    if (error) {
        NSLog(@"[ARObjCCamera] photo error: %@", error);
        [self finish];
        return;
    }

    NSData *data = [photo fileDataRepresentation];
    if (!data || data.length == 0) {
        NSLog(@"[ARObjCCamera] fileDataRepresentation is nil/empty");
        [self finish];
        return;
    }

    // フォトライブラリへ Add-Only で保存（NSError を確実に拾う）
    [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
        PHAssetCreationRequest *req = [PHAssetCreationRequest creationRequestForAsset];
        // データから直接追加（メタ情報保持に有利）
        [req addResourceWithType:PHAssetResourceTypePhoto data:data options:nil];
    } completionHandler:^(BOOL success, NSError * _Nullable err) {
        if (!success || err) {
            NSLog(@"[ARObjCCamera] save failed: %@", err);
        } else {
            NSLog(@"[ARObjCCamera] saved OK");
        }
    }];

    // 保存完了は待たずに終了（UI応答性優先）
    [self finish];
}
@end

// Add-Only 許可が「確定」してから block を実行
static void WithPhotoAddAuth(void (^block)(void)) {
    if (!block) return;
    if (@available(iOS 14, *)) {
        PHAuthorizationStatus st =
        [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelAddOnly];
        if (st == PHAuthorizationStatusAuthorized || st == PHAuthorizationStatusLimited) {
            dispatch_async(dispatch_get_main_queue(), block);
            return;
        }
        if (st == PHAuthorizationStatusDenied || st == PHAuthorizationStatusRestricted) {
            NSLog(@"[ARObjCCamera] photo auth denied/restricted");
            return;
        }
        [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly
                                                  handler:^(PHAuthorizationStatus s){
            if (s == PHAuthorizationStatusAuthorized || s == PHAuthorizationStatusLimited) {
                dispatch_async(dispatch_get_main_queue(), block);
            } else {
                NSLog(@"[ARObjCCamera] photo auth not granted");
            }
        }];
    } else {
        // iOS13以前
        PHAuthorizationStatus st = [PHPhotoLibrary authorizationStatus];
        if (st == PHAuthorizationStatusAuthorized) {
            dispatch_async(dispatch_get_main_queue(), block);
            return;
        }
        if (st == PHAuthorizationStatusDenied || st == PHAuthorizationStatusRestricted) {
            NSLog(@"[ARObjCCamera] photo auth denied/restricted (legacy)");
            return;
        }
        [PHPhotoLibrary requestAuthorization:^(PHAuthorizationStatus s){
            if (s == PHAuthorizationStatusAuthorized) {
                dispatch_async(dispatch_get_main_queue(), block);
            } else {
                NSLog(@"[ARObjCCamera] photo auth not granted (legacy)");
            }
        }];
    }
}

// Unity から 1 回だけネイティブ静止画を撮影して保存
extern "C" void ARNative_CaptureOneShot(void)
{
    WithPhotoAddAuth(^{
        // カメラ権限（AVCapture）チェック：NotDetermined はそのまま進める（別途OSダイアログ）
        AVAuthorizationStatus cs = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
        if (cs == AVAuthorizationStatusDenied || cs == AVAuthorizationStatusRestricted) {
            NSLog(@"[ARObjCCamera] camera not authorized");
            return;
        }

        // ARKit と競合しないよう、この時点で Unity 側は ARSession を一時停止している想定
        AVCaptureDevice *cam = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
        if (!cam) { NSLog(@"[ARObjCCamera] no camera"); return; }

        NSError *err = nil;
        AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:cam error:&err];
        if (!input || err) { NSLog(@"[ARObjCCamera] input err: %@", err); return; }

        _gPhotoDelegate = [_ARPhotoDelegate new];
        _gPhotoDelegate.session = [AVCaptureSession new];
        _gPhotoDelegate.output  = [AVCapturePhotoOutput new];

        // セッション構成
        [_gPhotoDelegate.session beginConfiguration];
        if ([_gPhotoDelegate.session canAddInput:input]) {
            [_gPhotoDelegate.session addInput:input];
        } else {
            NSLog(@"[ARObjCCamera] cannot add input");
            [_gPhotoDelegate finish];
            return;
        }
        if ([_gPhotoDelegate.session canAddOutput:_gPhotoDelegate.output]) {
            [_gPhotoDelegate.session addOutput:_gPhotoDelegate.output];
        } else {
            NSLog(@"[ARObjCCamera] cannot add output");
            [_gPhotoDelegate finish];
            return;
        }
        _gPhotoDelegate.session.sessionPreset = AVCaptureSessionPresetPhoto;
        [_gPhotoDelegate.session commitConfiguration];

        // セッション開始→1枚だけキャプチャ
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            @autoreleasepool {
                if (![_gPhotoDelegate.session isRunning]) {
                    [_gPhotoDelegate.session startRunning];
                }
                AVCapturePhotoSettings *settings = [AVCapturePhotoSettings photoSettings];
                // 高解像度を許可（端末により自動調整、非対応なら無視される）
                settings.highResolutionPhotoEnabled = YES;
                // 必要に応じてフラッシュや品質優先度をここで設定
                [_gPhotoDelegate.output capturePhotoWithSettings:settings delegate:_gPhotoDelegate];
            }
        });
    });
}

// Unity から渡された PNG バイト列をそのまま写真ライブラリへ保存（NSError取得ルート）
extern "C" void ARNative_SavePNGToPhotos(const unsigned char* data, int length)
{
    WithPhotoAddAuth(^{
        @autoreleasepool {
            if (!data || length <= 0) { NSLog(@"[ARObjCCamera] invalid PNG data"); return; }

            NSData *png = [NSData dataWithBytes:data length:(NSUInteger)length];
            if (!png || png.length == 0) { NSLog(@"[ARObjCCamera] PNG bytes invalid"); return; }

            UIImage *img = [UIImage imageWithData:png];
            if (!img) { NSLog(@"[ARObjCCamera] UIImage decode failed"); return; }

            // Photos.framework で保存（失敗理由が取れる & 例外不要）
            [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
                [PHAssetChangeRequest creationRequestForAssetFromImage:img];
            } completionHandler:^(BOOL success, NSError * _Nullable error) {
                if (!success || error) {
                    NSLog(@"[ARObjCCamera] save PNG failed: %@", error);
                } else {
                    NSLog(@"[ARObjCCamera] saved PNG to Photos");
                }
            }];
        }
    });
}