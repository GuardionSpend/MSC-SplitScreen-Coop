/*
 *  Split Screen Co-op  (My Summer Car mod)  v0.3
 *  Собран под MSCLoader 1.4.1, рантайм .NET 2.0/3.5 (как в игре).
 *  -------------------------------------------------------------
 *  Игрок 1 — клава+мышь (не трогаем). Игрок 2 — геймпад (XInput).
 *
 *  УПРАВЛЕНИЕ ИГРОКА 2 — ГЕЙМПАД (Xbox-совместимый):
 *    Левый стик ....... ходьба/бег
 *    Правый стик ...... поворот камеры
 *    A ................ прыжок
 *    B ................ присесть (держать) — становишься ниже/уже
 *    Y ................ сесть/встать (у машины — пассажиром)
 *    X ................ взять/положить предмет; на двери/кране — использовать
 *
 *  ЭКСПЕРИМЕНТАЛЬНО (взаимодействие игрока 2):
 *    - Предметы берутся СВОИМ физическим захватом (не трогаем «руку» игры),
 *      поэтому работает с любым физ-объектом, но не со сборкой/болтами.
 *    - Двери/выключатели дёргаются через SendMessage("OnMouseDown") —
 *      срабатывает на том, что использует мышиные события PlayMaker.
 *
 *  Резерв с КЛАВИАТУРЫ (нампад, не пересекается с игроком 1):
 *    8/2/4/6 движение, 7/9 поворот, 5 присед, 0 прыжок, . сесть.
 *
 *  F8 — диагностика (геймпад + слои объектов перед камерой игрока 1,
 *       чтобы убрать руку с пивом со 2-го экрана: впиши слой в HIDE_FROM_CAM2).
 */

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using MSCLoader;

namespace SplitScreenCoop
{
    public class SplitScreenCoop : Mod
    {
        public override string ID => "SplitScreenCoop";
        public override string Name => "Split Screen Co-op";
        public override string Author => "you";
        public override string Version => "0.5.0";
        public override string Description => "Сплит-скрин: игрок 2 на геймпаде, со смешными лицами.";

        // ======================= НАСТРОЙКИ =======================
        const float WALK_SPEED = 3.0f;
        const float RUN_SPEED  = 6.0f;
        const float LOOK_SPEED = 150.0f;
        const float GRAVITY    = 12.0f;
        const float JUMP_SPEED = 4.5f;

        const float STAND_HEIGHT = 1.8f, CROUCH_HEIGHT = 1.0f;
        const float BODY_RADIUS  = 0.24f; // уже, чтобы пролезать в дверь

        const bool  SPLIT_TOP_BOTTOM = true;

        const string SATSUMA_NAME = "SATSUMA(557kg, 248)";
        const float  SEAT_RANGE = 6f;
        static readonly Vector3 PASSENGER_OFFSET = new Vector3(0.30f, 0.55f, 0.20f);

        static readonly Color P1_COLOR = new Color(0.2f, 0.4f, 1f);
        static readonly Color P2_COLOR = new Color(1f, 0.25f, 0.2f);

        // Слой, который НЕ рендерить на 2-м экране (рука с пивом). -1 = выкл.
        const int HIDE_FROM_CAM2 = -1;
        // =========================================================

        GameObject p2;
        CharacterController cc;
        Camera cam2, mainCam;
        GameObject p1Body, p2Body;
        float pitch, vSpeed;
        bool seated, crouch;
        Transform satsuma;
        int p1Layer = -1, p2Layer = -1;
        ushort prevButtons;

        Transform holdPoint;     // куда цепляется взятый предмет
        Rigidbody heldRb;        // что держим
        bool heldWasKinematic;   // вернуть состояние при отпускании

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.Update, Mod_Update);
        }

        void Mod_OnLoad()
        {
            try
            {
                mainCam = Camera.main;
                // камера игрока 1 — ВСЕГДА на весь экран, иначе ломается
                // прицел/взаимодействие (MSC считает луч от полноэкранной камеры).
                if (mainCam != null) mainCam.rect = new Rect(0f, 0f, 1f, 1f);

                p2 = new GameObject("Player2_Root");
                cc = p2.AddComponent<CharacterController>();
                cc.height = STAND_HEIGHT; cc.radius = BODY_RADIUS;
                cc.center = new Vector3(0, STAND_HEIGHT / 2f, 0);
                if (mainCam != null)
                    p2.transform.position = mainCam.transform.position + mainCam.transform.forward * 1.5f;

                cam2 = new GameObject("Player2_Camera").AddComponent<Camera>();
                cam2.transform.SetParent(p2.transform);
                cam2.transform.localPosition = new Vector3(0, 1.6f, 0);
                cam2.transform.localRotation = Quaternion.identity;

                holdPoint = new GameObject("SSC_Hold").transform;
                holdPoint.SetParent(cam2.transform);
                holdPoint.localPosition = new Vector3(0f, -0.15f, 0.9f);
                holdPoint.localRotation = Quaternion.identity;

                p1Layer = FindFreeLayer(8);
                p2Layer = FindFreeLayer(p1Layer + 1);

                p2Body = MakeBody(P2_COLOR, p2Layer);
                p2Body.transform.SetParent(p2.transform);
                p2Body.transform.localPosition = Vector3.zero;

                p1Body = MakeBody(P1_COLOR, p1Layer);

                ApplyCulling();
                ApplyViewports();
                ModConsole.Print("[SSC] v0.3 загружен. Игрок 2 — геймпад, Y = сесть в машину, F8 = диагностика.");
                if (!XInputAvailable())
                    ModConsole.Print("[SSC] XInput недоступен — игрок 2 на нампаде (резерв).");
            }
            catch (Exception e)
            {
                ModConsole.Error("[SSC] ошибка OnLoad: " + e.Message + "\n" + e.StackTrace);
            }
        }

        void Mod_Update()
        {
            if (p2 == null) return;

            ApplyViewports();
            ApplyCulling();
            UpdateP1Marker();

            if (Input.GetKeyDown(KeyCode.F8)) Diagnostics();

            float mx, mz, lx, ly;
            bool jump, sit, crouchHeld, interact;
            ReadPlayer2(out mx, out mz, out lx, out ly, out jump, out sit, out crouchHeld, out interact);
            crouch = crouchHeld;

            if (sit) ToggleSeat();
            if (interact) DoInteract();   // взять/положить/использовать — можно и сидя

            HandleLook(lx, ly);            // крутить головой можно и сидя
            if (!seated) HandleMove(mx, mz, jump);
        }

        void ReadPlayer2(out float mx, out float mz, out float lx, out float ly,
                         out bool jump, out bool sit, out bool crouchHeld, out bool interact)
        {
            mx = mz = lx = ly = 0f; jump = sit = crouchHeld = interact = false;

            XINPUT_STATE st;
            if (TryGetXInput(0, out st))
            {
                var g = st.Gamepad;
                mx = Norm(g.sThumbLX, 7849f);
                mz = Norm(g.sThumbLY, 7849f);
                lx = Norm(g.sThumbRX, 8689f);
                ly = Norm(g.sThumbRY, 8689f);

                ushort b = g.wButtons;
                jump       = Pressed(b, 0x1000); // A
                sit        = Pressed(b, 0x8000); // Y
                interact   = Pressed(b, 0x4000); // X
                crouchHeld = (b & 0x2000) != 0;  // B
                prevButtons = b;
            }
            else
            {
                if (Input.GetKey(KeyCode.Keypad4)) mx -= 1f;
                if (Input.GetKey(KeyCode.Keypad6)) mx += 1f;
                if (Input.GetKey(KeyCode.Keypad8)) mz += 1f;
                if (Input.GetKey(KeyCode.Keypad2)) mz -= 1f;
                if (Input.GetKey(KeyCode.Keypad7)) lx -= 1f;
                if (Input.GetKey(KeyCode.Keypad9)) lx += 1f;
                jump       = Input.GetKeyDown(KeyCode.Keypad0);
                sit        = Input.GetKeyDown(KeyCode.KeypadPeriod);
                interact   = Input.GetKeyDown(KeyCode.KeypadEnter);
                crouchHeld = Input.GetKey(KeyCode.Keypad5);
            }
        }

        bool Pressed(ushort b, ushort mask) { return (b & mask) != 0 && (prevButtons & mask) == 0; }

        void HandleLook(float lx, float ly)
        {
            p2.transform.Rotate(0f, lx * LOOK_SPEED * Time.deltaTime, 0f);
            pitch = Mathf.Clamp(pitch - ly * LOOK_SPEED * Time.deltaTime, -80f, 80f);
            cam2.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void HandleMove(float mx, float mz, bool jump)
        {
            // присед уменьшает габарит, чтобы пролезать
            float targetH = crouch ? CROUCH_HEIGHT : STAND_HEIGHT;
            cc.height = targetH;
            cc.center = new Vector3(0, targetH / 2f, 0);
            cam2.transform.localPosition = new Vector3(0, targetH - 0.2f, 0);

            Vector3 dir = p2.transform.right * mx + p2.transform.forward * mz;
            float mag = Mathf.Clamp01(dir.magnitude);
            if (mag > 0.001f) dir /= dir.magnitude;
            float speed = mag * RUN_SPEED;
            if (crouch) speed = Mathf.Min(speed, WALK_SPEED);

            if (cc.isGrounded)
            {
                vSpeed = -1f;
                if (jump && !crouch) vSpeed = JUMP_SPEED;
            }
            else vSpeed -= GRAVITY * Time.deltaTime;

            Vector3 vel = dir * speed + Vector3.up * vSpeed;
            cc.Move(vel * Time.deltaTime);
        }

        void ToggleSeat()
        {
            if (!seated)
            {
                if (satsuma == null) satsuma = FindSatsuma();
                if (satsuma == null) { ModConsole.Print("[SSC] машина не найдена в сцене."); return; }
                float d = Vector3.Distance(p2.transform.position, satsuma.position);
                ModConsole.Print(string.Format("[SSC] Y нажата. До машины {0:0.0} м.", d));
                if (d > SEAT_RANGE) { ModConsole.Print("[SSC] подойди ближе к машине."); return; }
                cc.enabled = false;
                p2.transform.SetParent(satsuma);
                p2.transform.localPosition = PASSENGER_OFFSET;
                p2.transform.localRotation = Quaternion.identity;
                seated = true;
                ModConsole.Print("[SSC] сел пассажиром. Позицию правь PASSENGER_OFFSET.");
            }
            else
            {
                p2.transform.SetParent(null);
                p2.transform.position += Vector3.up * 0.2f;
                cc.enabled = true; seated = false;
                ModConsole.Print("[SSC] встал.");
            }
        }

        // ---------------- ВЗАИМОДЕЙСТВИЕ ИГРОКА 2 ----------------
        void DoInteract()
        {
            // держим предмет — отпускаем
            if (heldRb != null) { Release(); return; }
            if (cam2 == null) return;

            // луч из камеры, начиная чуть впереди (чтобы не задеть своё тело)
            Vector3 origin = cam2.transform.position + cam2.transform.forward * 0.35f;
            int mask = ~0;
            if (p2Layer >= 0) mask &= ~(1 << p2Layer);
            if (p1Layer >= 0) mask &= ~(1 << p1Layer);

            RaycastHit hit;
            if (!Physics.Raycast(origin, cam2.transform.forward, out hit, 2.2f, mask))
            { ModConsole.Print("[SSC] перед тобой ничего."); return; }

            Rigidbody rb = hit.collider.attachedRigidbody;
            bool grabbable = rb != null && !rb.isKinematic && rb.mass <= 40f
                             && !rb.name.ToUpper().Contains("SATSUMA");

            if (grabbable) Grab(rb);
            else UseObject(hit.collider.gameObject); // дверь/кран/выключатель
        }

        void Grab(Rigidbody rb)
        {
            heldRb = rb;
            heldWasKinematic = rb.isKinematic;
            rb.isKinematic = true;
            rb.transform.SetParent(holdPoint);
            rb.transform.localPosition = Vector3.zero;
            ModConsole.Print("[SSC] взял: " + rb.name);
        }

        void Release()
        {
            if (heldRb == null) return;
            heldRb.transform.SetParent(null);
            heldRb.isKinematic = heldWasKinematic;
            // лёгкий толчок вперёд, чтобы предмет не залип в руке
            heldRb.velocity = cam2.transform.forward * 1.0f;
            ModConsole.Print("[SSC] положил: " + heldRb.name);
            heldRb = null;
        }

        void UseObject(GameObject go)
        {
            // имитируем мышиное взаимодействие PlayMaker (двери/краны/выключатели)
            go.SendMessage("OnMouseDown", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnMouseUp", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("OnMouseUpAsButton", SendMessageOptions.DontRequireReceiver);
            ModConsole.Print("[SSC] использую: " + go.name);
        }

        // ---------------- КАМЕРЫ / СЛОИ ----------------
        void ApplyViewports()
        {
            if (mainCam == null) mainCam = Camera.main;
            if (cam2 == null) return;
            // НЕ трогаем rect камеры игрока 1 (full screen) — иначе ломается прицел.
            // Камера игрока 2 рисуется ПОВЕРХ половины экрана (выше по depth).
            cam2.depth = (mainCam != null ? mainCam.depth : 0) + 1;
            cam2.rect = SPLIT_TOP_BOTTOM ? new Rect(0f, 0f, 1f, 0.5f)   // нижняя половина
                                         : new Rect(0.5f, 0f, 0.5f, 1f); // правая половина
        }

        void ApplyCulling()
        {
            if (cam2 == null) return;
            int mask = ~0;
            if (p2Layer >= 0) mask &= ~(1 << p2Layer);
            if (HIDE_FROM_CAM2 >= 0) mask &= ~(1 << HIDE_FROM_CAM2);
            cam2.cullingMask = mask;
            if (mainCam != null && p1Layer >= 0)
                mainCam.cullingMask &= ~(1 << p1Layer);
        }

        void UpdateP1Marker()
        {
            if (p1Body == null || mainCam == null) return;
            Vector3 p = mainCam.transform.position; p.y -= 1.5f;
            p1Body.transform.position = p;
            p1Body.transform.rotation = Quaternion.Euler(0f, mainCam.transform.eulerAngles.y, 0f);
        }

        // ---------------- ТЕЛО + СМЕШНОЕ ЛИЦО ----------------
        GameObject MakeBody(Color col, int layer)
        {
            GameObject root = new GameObject("SSC_Body");

            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            KillCollider(cap);
            cap.transform.SetParent(root.transform);
            cap.transform.localPosition = new Vector3(0, 0.9f, 0);
            cap.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
            Paint(cap, col);

            // лицо на «голове» (верх капсулы), смотрит вперёд (+z)
            float eyeY = 1.55f, front = 0.30f;
            // большие белые глаза
            FacePart(root, PrimitiveType.Sphere, Color.white, new Vector3(-0.13f, eyeY, front), 0.20f);
            FacePart(root, PrimitiveType.Sphere, Color.white, new Vector3( 0.13f, eyeY, front), 0.20f);
            // чёрные зрачки чуть впереди
            FacePart(root, PrimitiveType.Sphere, Color.black, new Vector3(-0.13f, eyeY, front + 0.07f), 0.09f);
            FacePart(root, PrimitiveType.Sphere, Color.black, new Vector3( 0.13f, eyeY, front + 0.07f), 0.09f);
            // рот — широкая чёрная полоска
            GameObject mouth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            KillCollider(mouth);
            mouth.transform.SetParent(root.transform);
            mouth.transform.localPosition = new Vector3(0f, eyeY - 0.28f, front + 0.02f);
            mouth.transform.localScale = new Vector3(0.26f, 0.06f, 0.05f);
            Paint(mouth, Color.black);

            SetLayerRecursive(root, layer);
            return root;
        }

        void FacePart(GameObject parent, PrimitiveType t, Color c, Vector3 lpos, float scale)
        {
            GameObject g = GameObject.CreatePrimitive(t);
            KillCollider(g);
            g.transform.SetParent(parent.transform);
            g.transform.localPosition = lpos;
            g.transform.localScale = Vector3.one * scale;
            Paint(g, c);
        }

        void Paint(GameObject g, Color c)
        {
            var r = g.GetComponent<Renderer>();
            if (r == null) return;
            Shader sh = Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Diffuse");
            if (sh != null) r.material = new Material(sh);
            r.material.color = c;
        }

        void KillCollider(GameObject g)
        {
            var col = g.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }

        void SetLayerRecursive(GameObject g, int layer)
        {
            if (layer < 0) return;
            g.layer = layer;
            foreach (Transform t in g.transform) SetLayerRecursive(t.gameObject, layer);
        }

        int FindFreeLayer(int start)
        {
            for (int i = Mathf.Max(8, start); i < 32; i++)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return -1;
        }

        Transform FindSatsuma()
        {
            GameObject g = GameObject.Find(SATSUMA_NAME);
            if (g != null) return g.transform;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t.name.Contains("SATSUMA")) return t;
            return null;
        }

        void Diagnostics()
        {
            ModConsole.Print("===== SSC диагностика =====");
            XINPUT_STATE st;
            if (TryGetXInput(0, out st))
                ModConsole.Print(string.Format("Геймпад#0 OK: LX={0} LY={1} RX={2} RY={3} btn=0x{4:X4}",
                    st.Gamepad.sThumbLX, st.Gamepad.sThumbLY, st.Gamepad.sThumbRX, st.Gamepad.sThumbRY, st.Gamepad.wButtons));
            else
                ModConsole.Print("Геймпад#0: не подключён / XInput недоступен.");

            if (satsuma == null) satsuma = FindSatsuma();
            ModConsole.Print("Машина: " + (satsuma != null ? "найдена" : "НЕ найдена"));

            if (mainCam != null)
            {
                ModConsole.Print("Объекты с рендером перед камерой игрока 1 (слой для HIDE_FROM_CAM2):");
                foreach (Transform t in mainCam.GetComponentsInChildren<Transform>())
                    if (t.GetComponent<Renderer>() != null)
                        ModConsole.Print(string.Format("  {0} -> слой {1} ({2})",
                            t.name, t.gameObject.layer, LayerMask.LayerToName(t.gameObject.layer)));
            }
            ModConsole.Print("===========================");
        }

        // ---------------- XInput ----------------
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger, bRightTrigger;
            public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

        [DllImport("xinput9_1_0.dll")]
        static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

        static bool xinputBroken;
        static bool XInputAvailable() { XINPUT_STATE s; return TryGetXInput(0, out s) || !xinputBroken; }
        static bool TryGetXInput(uint idx, out XINPUT_STATE st)
        {
            st = new XINPUT_STATE();
            if (xinputBroken) return false;
            try { return XInputGetState(idx, ref st) == 0; }
            catch { xinputBroken = true; return false; }
        }

        static float Norm(short v, float dz)
        {
            float f = v;
            if (Mathf.Abs(f) < dz) return 0f;
            float sign = Mathf.Sign(f);
            return sign * Mathf.Clamp01((Mathf.Abs(f) - dz) / (32767f - dz));
        }
    }
}
