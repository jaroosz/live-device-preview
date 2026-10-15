using System;
using System.Runtime.InteropServices;
using System.Buffers;

namespace MobiView.Editor.Capture.Encoding
{
    public enum VideoCodec
    {
        H264,
        H265
    }

    /// <summary>
    /// NVIDIA NVENC H.265/H.264 encoder with D3D11 texture input.
    /// Adapted for Unity Editor.
    /// </summary>
    public unsafe class NvencEncoder : IDisposable
    {
        private void* _encoder;
        private void* _d3d11Device;
        private void* _bitstreamBuffer;
        private void* _registeredResource;
        private bool _initialized;

        private NvEncodeAPI.NV_ENCODE_API_FUNCTION_LIST* _apiPtr;

        private readonly int _width;
        private readonly int _height;
        private readonly int _bitrate;
        private readonly int _fps;
        private readonly VideoCodec _codec;
        private uint _frameIndex;

        private int _encodedFrameCount;
        private bool _forceNextIDR = false;

        public void ForceNextIDR() => _forceNextIDR = true;

        public NvencEncoder(
            IntPtr d3d11Device, 
            int width, int height, 
            VideoCodec codec = VideoCodec.H265, 
            int bitrate = 15_000_000,
            int fps = 60)
        {
            _d3d11Device = (void*)d3d11Device;
            _width = width;
            _height = height;
            _bitrate = bitrate;
            _fps = Math.Max(1, fps);
            _codec = codec;

            Initialize();
        }

        private static void* AllocZeroed(int size)
        {
            IntPtr ptr = Marshal.AllocHGlobal(size);
            byte* b = (byte*)ptr;
            for(int i=0; i<size; i++) b[i] = 0;
            return (void*)ptr;
        }

        private static void FreeMemory(void* ptr)
        {
            if (ptr != null)
                Marshal.FreeHGlobal((IntPtr)ptr);
        }

        private void Initialize()
        {
            try
            {
                _apiPtr = (NvEncodeAPI.NV_ENCODE_API_FUNCTION_LIST*)AllocZeroed(sizeof(NvEncodeAPI.NV_ENCODE_API_FUNCTION_LIST));
                _apiPtr->version = NvEncodeAPI.StructVersion(2);

                var status = NvEncodeAPI.NvEncodeAPICreateInstance(_apiPtr);
                CheckStatus(status, "NvEncodeAPICreateInstance");

                OpenEncodeSession();

                var config = GetPresetConfig();
                ConfigureCodec(ref config);

                InitializeEncoder(CreateInitParams(config));

                CreateBitstreamBuffer();

                _initialized = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"NVENC Init FAILED: {ex.Message}\n{ex.StackTrace}");
                CleanupResources();
                throw;
            }
        }

        private void OpenEncodeSession()
        {
            var openParams = NvEncodeAPI.NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS.Create();
            openParams.deviceType = NvEncodeAPI.NV_ENC_DEVICE_TYPE.NV_ENC_DEVICE_TYPE_DIRECTX;
            openParams.device = _d3d11Device;

            var openSessionEx = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncOpenEncodeSessionEx>(
                (IntPtr)_apiPtr->nvEncOpenEncodeSessionEx);

            void* encoderTemp = null;
            var status = openSessionEx(&openParams, &encoderTemp);
            CheckStatus(status, "NvEncOpenEncodeSessionEx");
            _encoder = encoderTemp;
        }

        private NvEncodeAPI.NV_ENC_CONFIG GetPresetConfig()
        {
            var presetConfig = NvEncodeAPI.NV_ENC_PRESET_CONFIG.Create();

            var getPresetConfig = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncGetEncodePresetConfigEx>(
                (IntPtr)_apiPtr->nvEncGetEncodePresetConfigEx);

            NvEncodeAPI.GUID codecGuid = _codec switch
            {
                VideoCodec.H264 => NvEncodeAPI.GUID.H264,
                VideoCodec.H265 => NvEncodeAPI.GUID.HEVC,
                _ => NvEncodeAPI.GUID.HEVC
            };

            NvEncodeAPI.GUID p1Guid = NvEncodeAPI.GUID.Preset_P1;

            var status = getPresetConfig(
                _encoder,
                codecGuid,
                p1Guid,
                NvEncodeAPI.NV_ENC_TUNING_INFO.NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY,
                &presetConfig);

            CheckStatus(status, "NvEncGetEncodePresetConfigEx");

            return presetConfig.presetCfg;
        }

        private void ConfigureCodec(ref NvEncodeAPI.NV_ENC_CONFIG config)
        {
            config.gopLength = (uint)(_fps * 2);
            config.frameIntervalP = 1;

            if (_codec == VideoCodec.H265)
            {
                config.encodeCodecConfig.hevcConfig.idrPeriod = (uint)(_fps * 2);
                config.encodeCodecConfig.hevcConfig.repeatSPSPPS = 1;
                config.encodeCodecConfig.hevcConfig.outputAUD = 1;
                config.encodeCodecConfig.hevcConfig.enableLTR = 0;
                config.encodeCodecConfig.hevcConfig.ltrNumFrames = 0;
                config.encodeCodecConfig.hevcConfig.maxNumRefFramesInDPB = 1;
            }

            config.rcParams.rateControlMode = NvEncodeAPI.NV_ENC_PARAMS_RC_MODE.NV_ENC_PARAMS_RC_CBR;
            config.rcParams.averageBitRate = (uint)_bitrate;
            config.rcParams.maxBitRate = (uint)_bitrate;
            config.rcParams.vbvBufferSize = (uint)(_bitrate / _fps);
            config.rcParams.vbvInitialDelay = 0;
        }

        private NvEncodeAPI.NV_ENC_INITIALIZE_PARAMS CreateInitParams(NvEncodeAPI.NV_ENC_CONFIG config)
        {
            NvEncodeAPI.GUID codecGuid = _codec switch
            {
                VideoCodec.H264 => NvEncodeAPI.GUID.H264,
                VideoCodec.H265 => NvEncodeAPI.GUID.HEVC,
                _ => NvEncodeAPI.GUID.HEVC
            };
            NvEncodeAPI.GUID p1Guid = NvEncodeAPI.GUID.Preset_P1;

            var initParams = NvEncodeAPI.NV_ENC_INITIALIZE_PARAMS.Create();
            initParams.encodeGUID = codecGuid;
            initParams.presetGUID = p1Guid;
            initParams.encodeWidth = (uint)_width;
            initParams.encodeHeight = (uint)_height;
            initParams.darWidth = (uint)_width;
            initParams.darHeight = (uint)_height;
            initParams.frameRateNum = (uint)_fps;
            initParams.frameRateDen = 1;
            initParams.enablePTD = 1;
            initParams.tuningInfo = NvEncodeAPI.NV_ENC_TUNING_INFO.NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;
            initParams.bufferFormat = NvEncodeAPI.NV_ENC_BUFFER_FORMAT.NV_ENC_BUFFER_FORMAT_ARGB;

            var configPtr = (NvEncodeAPI.NV_ENC_CONFIG*)AllocZeroed(sizeof(NvEncodeAPI.NV_ENC_CONFIG));
            *configPtr = config;
            initParams.encodeConfig = configPtr;

            return initParams;
        }

        private void InitializeEncoder(NvEncodeAPI.NV_ENC_INITIALIZE_PARAMS initParams)
        {
            var initEncoder = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncInitializeEncoder>(
                (IntPtr)_apiPtr->nvEncInitializeEncoder);

            try
            {
                var status = initEncoder(_encoder, &initParams);
                CheckStatus(status, "NvEncInitializeEncoder");
            }
            finally
            {
                FreeMemory(initParams.encodeConfig);
            }
        }

        private void CreateBitstreamBuffer()
        {
            var createBitstream = NvEncodeAPI.NV_ENC_CREATE_BITSTREAM_BUFFER.Create();

            var createBitstreamBuffer = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncCreateBitstreamBuffer>(
                (IntPtr)_apiPtr->nvEncCreateBitstreamBuffer);

            var status = createBitstreamBuffer(_encoder, &createBitstream);
            CheckStatus(status, "NvEncCreateBitstreamBuffer");

            _bitstreamBuffer = createBitstream.bitstreamBuffer;
        }

        public (byte[] Buffer, int Length, uint FrameNumber)? EncodeTexture(IntPtr d3d11Texture)
        {
            if (!_initialized) return null;

            var texturePtr = (void*)d3d11Texture;
            void* mappedResource = null;

            try
            {
                if (_registeredResource == null)
                {
                    var registerParams = NvEncodeAPI.NV_ENC_REGISTER_RESOURCE.Create();
                    registerParams.resourceType = NvEncodeAPI.NV_ENC_INPUT_RESOURCE_TYPE.NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
                    registerParams.resourceToRegister = texturePtr;
                    registerParams.width = (uint)_width;
                    registerParams.height = (uint)_height;
                    registerParams.pitch = 0;
                    registerParams.bufferFormat = NvEncodeAPI.NV_ENC_BUFFER_FORMAT.NV_ENC_BUFFER_FORMAT_ARGB;

                    var registerResource = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncRegisterResource>(
                        (IntPtr)_apiPtr->nvEncRegisterResource);

                    var regStatus = registerResource(_encoder, &registerParams);
                    CheckStatus(regStatus, "NvEncRegisterResource");
                    _registeredResource = registerParams.registeredResource;
                }

                var mapParams = NvEncodeAPI.NV_ENC_MAP_INPUT_RESOURCE.Create();
                mapParams.registeredResource = _registeredResource;

                var mapInputResource = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncMapInputResource>(
                    (IntPtr)_apiPtr->nvEncMapInputResource);

                var mapStatus = mapInputResource(_encoder, &mapParams);
                CheckStatus(mapStatus, "NvEncMapInputResource");
                mappedResource = mapParams.mappedResource;

                var picParams = NvEncodeAPI.NV_ENC_PIC_PARAMS.Create();
                picParams.inputBuffer = mappedResource;
                picParams.bufferFmt = NvEncodeAPI.NV_ENC_BUFFER_FORMAT.NV_ENC_BUFFER_FORMAT_ARGB;
                picParams.inputWidth = (uint)_width;
                picParams.inputHeight = (uint)_height;
                picParams.inputPitch = 0;
                picParams.outputBitstream = _bitstreamBuffer;
                picParams.completionEvent = null;
                picParams.pictureStruct = NvEncodeAPI.NV_ENC_PIC_STRUCT.NV_ENC_PIC_STRUCT_FRAME;

                if (_frameIndex == 0 || _forceNextIDR)
                {
                    picParams.pictureType = NvEncodeAPI.NV_ENC_PIC_TYPE.NV_ENC_PIC_TYPE_IDR;
                    _forceNextIDR = false;
                }

                picParams.frameIdx = _frameIndex;
                picParams.inputTimeStamp = (ulong)DateTime.UtcNow.Ticks;
                _frameIndex++;

                var encodePicture = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncEncodePicture>(
                    (IntPtr)_apiPtr->nvEncEncodePicture);

                var encodeStatus = encodePicture(_encoder, &picParams);

                if (encodeStatus == NvEncodeAPI.NVENCSTATUS.NV_ENC_ERR_NEED_MORE_INPUT)
                    return null;

                CheckStatus(encodeStatus, "NvEncEncodePicture");

                var lockParams = NvEncodeAPI.NV_ENC_LOCK_BITSTREAM.Create();
                lockParams.outputBitstream = _bitstreamBuffer;

                var lockBitstream = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncLockBitstream>(
                    (IntPtr)_apiPtr->nvEncLockBitstream);

                var lockStatus = lockBitstream(_encoder, &lockParams);
                CheckStatus(lockStatus, "NvEncLockBitstream");

                var encodedSize = (int)lockParams.bitstreamSizeInBytes;
                _encodedFrameCount++;

                var buffer = ArrayPool<byte>.Shared.Rent(encodedSize);
                Marshal.Copy((IntPtr)lockParams.bitstreamBufferPtr, buffer, 0, encodedSize);

                var unlockBitstream = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncUnlockBitstream>(
                    (IntPtr)_apiPtr->nvEncUnlockBitstream);
                unlockBitstream(_encoder, _bitstreamBuffer);

                return (buffer, encodedSize, (uint)_encodedFrameCount);
            }
            finally
            {
                if (mappedResource != null)
                {
                    var unmapInputResource = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncUnmapInputResource>(
                        (IntPtr)_apiPtr->nvEncUnmapInputResource);
                    unmapInputResource(_encoder, mappedResource);
                }
            }
        }

        private static void CheckStatus(NvEncodeAPI.NVENCSTATUS status, string operation)
        {
            if (status == NvEncodeAPI.NVENCSTATUS.NV_ENC_SUCCESS) return;
            throw new Exception($"NVENC {operation} failed: {status}");
        }

        private void CleanupResources()
        {
            try
            {
                if (_registeredResource != null && _apiPtr != null && _encoder != null)
                {
                    var unregisterResource = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncUnregisterResource>(
                        (IntPtr)_apiPtr->nvEncUnregisterResource);
                    unregisterResource(_encoder, _registeredResource);
                    _registeredResource = null;
                }

                if (_bitstreamBuffer != null && _apiPtr != null && _encoder != null)
                {
                    var destroyBitstream = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncDestroyBitstreamBuffer>(
                        (IntPtr)_apiPtr->nvEncDestroyBitstreamBuffer);
                    destroyBitstream(_encoder, _bitstreamBuffer);
                    _bitstreamBuffer = null;
                }

                if (_encoder != null && _apiPtr != null)
                {
                    var destroyEncoder = Marshal.GetDelegateForFunctionPointer<NvEncodeAPI.NvEncDestroyEncoder>(
                        (IntPtr)_apiPtr->nvEncDestroyEncoder);
                    destroyEncoder(_encoder);
                    _encoder = null;
                }

                if (_apiPtr != null)
                {
                    FreeMemory(_apiPtr);
                    _apiPtr = null;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Cleanup error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_initialized) return;

            CleanupResources();
            _initialized = false;
            GC.SuppressFinalize(this);
        }
    }
}

