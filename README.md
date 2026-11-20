# Around the World: AR Knickknacks

Two augmented-reality souvenirs ("knickknacks") that sit on paper cubes and show live information about the place they stand for:

| Cube | Attraction | Weather | Clock |
|------|-----------|---------|-------|
| **Merge Cube** | Eiffel Tower, Paris, France | °F, updated every 10 min | 12-hour (am/pm) |
| **Class Cube** | Belém Tower (Torre de Belém), Lisbon, Portugal | °C, updated every 10 min | 24-hour |

Each cube has the attraction's name on one side, the current weather on another, the local time on a third, and a "Did you know?" fact on the fourth. On top sit the 3D models, with an ambient sound. **Turn a cube upside down and back up** to switch its lighting (Paris: sunny afternoon / night with the tower sparkling and a rotating beacon; Lisbon: sunny day / sunset with lanterns). Do it again to switch back. Both cubes work at the same time.

## Software versions

* Unity **2021.3.6f1** (with Android Build Support, including OpenJDK and Android SDK & NDK, and/or iOS Build Support)
* Vuforia Engine **10.9.3**
* TextMesh Pro 3.0.6 (installed automatically by Unity)
* Android 8.0 (API 26) or newer phone, or a webcam on Windows/macOS

## Build and run

1. Install **Unity Hub**, then install Unity **2021.3.6f1** from the [Unity download archive](https://unity.com/releases/editor/archive). Add the **Android Build Support** module, with its OpenJDK and Android SDK & NDK sub-modules.
2. Clone or download this repository.
3. **Vuforia package:** the package file is too big for GitHub. Download *Vuforia Engine 10.9.3 for Unity* from the [Vuforia developer portal](https://developer.vuforia.com/downloads/sdk), section "Previous Versions". Put `com.ptc.vuforia.engine-10.9.3.tgz` in the project's `Packages/` folder. If you downloaded the `.unitypackage` instead, open the project and double-click it, and it copies the `.tgz` into `Packages/` for you.
4. In Unity Hub choose **Open → Add project from disk** and select this folder. The first import takes a few minutes.
5. **Vuforia license:** create a free account at [developer.vuforia.com](https://developer.vuforia.com), then go to *Plan & Licenses → License Manager → Generate Basic License*. Copy the key. In Unity open **Window → Vuforia Configuration** and paste it into **App License Key**.
6. **Weather key:** create a free account at [openweathermap.org](https://home.openweathermap.org/users/sign_up), copy your API key, and paste it between the quotes in `Assets/AroundTheWorld/Scripts/ApiKeys.cs`. New keys can take up to 2 hours to start working.
7. Open `Assets/Scenes/AroundTheWorld.unity`. If it does not exist yet, use the menu **Around The World → Build Scene** to generate it.
8. Print and fold the two cubes in `CubesForVuforia` (Merge cube and Class cube).
9. **Webcam:** press Play and hold the cubes in front of the camera.
10. **Android phone:** turn on Developer options and USB debugging on the phone and plug it in. Go to *File → Build Settings → Android → Switch Platform*, then *Build And Run*. The scene, IL2CPP/ARM64 and minimum API 26 are already set by the builder.
11. **iPhone:** *File → Build Settings → iOS → Switch Platform → Build*. Open the generated Xcode project, set your signing team and run it.

## Project layout

```
Assets/AroundTheWorld/
  Scripts/   KnickknackTarget (attaches content to a cube, plays sound while tracked)
             KnickknackWeather (OpenWeather, °F/°C, refresh every 10 min)
             KnickknackClock (worldtimeapi.org with OpenWeather/fixed-offset fallback, 12h/24h)
             KnickknackFlipLighting (upside-down detection, switches lighting styles)
             KnickknackSpin / Bob / Orbit / Twinkle (animations)
             ApiKeys (your OpenWeather key)
  Editor/    AroundTheWorldBuilder (menu "Around The World > Build Scene")
  MyModels/  models I built myself (prefabs)
  WebModels/Paris, WebModels/Belem   downloaded models (3 per attraction)
  Sounds/Paris, Sounds/Belem         ambient sounds
```

The builder makes the scene from the Roswell starter scene. You can run it again whenever you add models or sounds to the `WebModels` / `Sounds` folders. Each knickknack lives on its own layer (`Paris`, `Belem`), so its lights only affect its own models.

## Credits

### Models from the web
Paris (Merge cube):
1. *TODO: model name* by *author*, *URL*
2. *TODO*
3. *TODO*

Lisbon (Class cube):
1. *TODO*
2. *TODO*
3. *TODO*

### Models I created
1. Eiffel Tower (`MyModel1_EiffelTower`)
2. Parisian street lamp (`MyModel2_ParisStreetLamp`)
3. Belém Tower (`MyModel3_BelemTower`)
4. Portuguese caravel with the Cross of the Order of Christ on the sails (`MyModel4_Caravel`)

### Sounds
* Paris: *TODO (or "procedurally generated music box loop")*
* Lisbon: *TODO (or "procedurally generated Tagus river waves")*

### Other
* Starter project, Merge cube and Class cube images by Andy Johnson, CS 428 UIC
* Weather data: [OpenWeather](https://openweathermap.org); time: [worldtimeapi.org](https://worldtimeapi.org)
