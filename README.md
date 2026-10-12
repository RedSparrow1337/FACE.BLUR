# Faceblur (SPT 4.1.6) — клиентский BepInEx-плагин

API проверен по Assembly-CSharp.dll пользователя: все используемые поля/свойства/роли существуют.

## Шаги
1. Visual Studio 2022 (или .NET SDK): открыть Faceblur.csproj, в <SPT> указать путь к SPT, собрать Release.
   DLL сама скопируется в SPT\BepInEx\plugins\Faceblur\.
2. Unity 2022.3.x (Built-in RP): Assets\Faceblur\Faceblur.shader, Assets\Editor\BuildBundle.cs,
   меню Faceblur -> Build Bundle, взять Assets\Out\faceblur.bundle.
3. Положить faceblur.bundle в SPT\BepInEx\plugins\Faceblur\ рядом с Faceblur.dll.
4. Запустить SPT, зайти в рейд. Настройки: F12 -> Faceblur.
