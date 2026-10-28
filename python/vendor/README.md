# python/vendor

Put **`BioRadioSDK.dll`** here (Windows only). It comes from the course's BioRadio kit:
`BioRadio_Student_Unity_Kit/Python_Bridge/BioRadioSDK.dll` (also inside `SDK/Original_BioRadio_SDK.zip` as `API/BioRadioSDK.dll`).

Everything in this folder except this README is git-ignored: the kit says to keep the SDK within the institute,
and this repository is public. If Windows blocked the downloaded file: right-click it → Properties → **Unblock**.

The bridge finds it through `config.yaml` → `bioradio.sdk_dll` (default `vendor/BioRadioSDK.dll`).
