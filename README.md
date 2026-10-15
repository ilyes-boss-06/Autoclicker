# AutoClicker

Autoclicker para Windows inspirado en OP Auto Clicker, con grabación y reproducción de clics y una interfaz oscura moderna. Es una aplicación nativa (C# + WPF sobre .NET Framework 4.8) que no necesita instalar nada: el compilador y el runtime ya vienen con Windows.

## Funciones

**Clicker**
- Intervalo en horas, minutos, segundos y milisegundos, con temporización precisa que no se desvía.
- Botón izquierdo, derecho o central; clic simple o doble.
- Repetir hasta detener o un número exacto de veces.
- Clic donde esté el cursor o en un punto fijo, elegido con un selector a pantalla completa (multimonitor).

**Grabación y reproducción**
- Graba clics, clics derechos/centrales y arrastres con su posición y tiempos.
- Opcionalmente graba también el movimiento del ratón.
- Ignora los clics sobre la propia ventana y los clics sintéticos.
- Reproduce un número de veces o en bucle, a 0,5×, 1×, 2× o 4×.
- Guarda y abre macros (`.acmacro`); la última grabación se conserva al cerrar.

**General**
- Atajos de teclado globales y configurables (funcionan con la ventana en segundo plano).
- Opción de mantener la ventana siempre visible.
- Recuerda la configuración y la posición de la ventana.
- Compatible con escalado de pantalla (DPI por monitor).

## Atajos por defecto

| Acción | Tecla |
| --- | --- |
| Iniciar / detener el clicker | `INS` |
| Empezar / terminar grabación | `F7` |
| Reproducir / detener | `F9` |

Se cambian en la pestaña **Ajustes**.

## Compilar

Requisitos: Windows 10/11 (incluye .NET Framework 4.8).

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

El ejecutable queda en `bin\AutoClicker.exe`. El script genera antes `app.ico` y `src\logo.png` a partir de `assets\icon-source.jpg`.

## Estructura

| Archivo | Contenido |
| --- | --- |
| `src/MainWindow.xaml` | Interfaz (estilos y diseño) |
| `src/MainController.cs` | Lógica de la ventana, clicker, reproducción y atajos |
| `src/Macro.cs` | Grabadora (hook de ratón de bajo nivel) y formato de macros |
| `src/PickerForm.cs` | Selector de punto a pantalla completa |
| `src/Native.cs` | Llamadas a la API de Windows (`SendInput`, hotkeys, hooks, DWM) |
| `src/Settings.cs` | Configuración persistente |

## Notas

- Los ajustes y la última grabación se guardan en `%APPDATA%\AutoClicker`.
- Para hacer clic en programas abiertos como administrador, AutoClicker también debe ejecutarse como administrador (restricción de Windows).

## Licencia

[MIT](LICENSE)
