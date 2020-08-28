//  Beat Saber Custom Avatars - Custom player models for body presence in Beat Saber.
//  Copyright © 2018-2020  Beat Saber Custom Avatars Contributors
//
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.

using CustomAvatar.Avatar;
using CustomAvatar.Logging;
using CustomAvatar.Utilities;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

namespace CustomAvatar.Lighting
{
    internal class GameplayLightingController : MonoBehaviour
    {
        // TODO this should be adjusted according to room config
        private static readonly Vector3 kOrigin = new Vector3(0, 1, 0);

        private ILogger<GameplayLightingController> _logger;
        private LightWithIdManager _lightManager;
        private ColorManager _colorManager;
        private PlayerController _playerController;
        private TwoSidedLightingController _twoSidedLightingController;

        private List<GameLight>[] _lights;
        
        #region Behaviour Lifecycle
        #pragma warning disable IDE0051
        // ReSharper disable UnusedMember.Local

        [Inject]
        private void Inject(ILoggerProvider loggerProvider, LightWithIdManager lightManager, ColorManager colorManager, PlayerController playerController, TwoSidedLightingController twoSidedLightingController)
        {
            _logger = loggerProvider.CreateLogger<GameplayLightingController>();
            _lightManager = lightManager;
            _colorManager = colorManager;
            _playerController = playerController;
            _twoSidedLightingController = twoSidedLightingController;

            _lightManager.didSetColorForIdEvent += OnSetColorForId;
        }

        private void Start()
        {
            _twoSidedLightingController.gameObject.SetActive(false);

            CreateLights();

            AddPointLight(_colorManager.ColorForSaberType(SaberType.SaberA), _playerController.leftSaber.transform);
            AddPointLight(_colorManager.ColorForSaberType(SaberType.SaberB), _playerController.rightSaber.transform);
        }

        private void Update()
        {
            for (int id = 0; id < _lights.Length; id++)
            {
                if (_lights[id] == null) continue;

                foreach (GameLight gameLight in _lights[id])
                {
                    gameLight.light.transform.LookAt(kOrigin - (gameLight.tubeLight.transform.position + gameLight.offset));
                }
            }
        }

        private void OnDestroy()
        {
            _twoSidedLightingController.gameObject.SetActive(true);
        }

        // ReSharper disable UnusedMember.Local
        #pragma warning restore IDE0051
        #endregion

        private void CreateLights()
        {
            List<LightWithId>[] lightsWithId = _lightManager.GetPrivateField<List<LightWithId>[]>("_lights");
            int maxLightId = _lightManager.GetPrivateField<int>("kMaxLightId");

            _lights = new List<GameLight>[maxLightId + 1];
            
            for (int id = 0; id < lightsWithId.Length; id++)
            {
                if (lightsWithId[id] == null) continue;

                foreach (LightWithId lightWithId in lightsWithId[id])
                {
                    Vector3 direction = kOrigin - lightWithId.transform.position;

                    var light = new GameObject("DynamicLight").AddComponent<Light>();

                    light.type = LightType.Directional;
                    light.color = Color.black;
                    light.shadows = LightShadows.None; // shadows murder fps since there's so many lights being added
                    light.renderMode = LightRenderMode.ForceVertex; // reduce performance toll
                    light.intensity = 0;
                    light.spotAngle = 45;
                    light.cullingMask = AvatarLayers.kAllLayersMask;

                    light.transform.SetParent(transform);
                    light.transform.position = Vector3.zero;
                    light.transform.rotation = Quaternion.identity;

                    if (_lights[id] == null)
                    {
                        _lights[id] = new List<GameLight>(10);
                    }

                    foreach (TubeBloomPrePassLight tubeLight in lightWithId.GetComponentsInChildren<TubeBloomPrePassLight>())
                    {
                        _lights[id].Add(new GameLight(tubeLight, light));
                    }
                }
            }

            _logger.Trace($"Created {_lights.Sum(l => l?.Count)} lights");
        }

        private void OnSetColorForId(int id, Color color)
        {
            if (_lights[id] == null) return;

            foreach (GameLight light in _lights[id])
            {
                if (light.tubeLight.isActiveAndEnabled)
                {
                    Vector3 position = light.tubeLight.transform.position;
                    Vector3 up = (light.tubeLight.transform.rotation * Vector3.up).normalized;

                    Vector3 m = Vector3.Cross(position, up);
                    float sqrMinimumDistance = Vector3.Cross(position, up).magnitude;
                    float minimumDistance = Mathf.Sqrt(sqrMinimumDistance);

                    // the two ends of the light
                    Vector3 endA = position + (1.0f - light.center) * light.length * up;
                    Vector3 endB = position - light.center * light.length * up;

                    float x0 = Mathf.Sqrt(endA.sqrMagnitude - sqrMinimumDistance) * Mathf.Sign(Vector3.Dot(endA - m, up));
                    float x1 = Mathf.Sqrt(endB.sqrMagnitude - sqrMinimumDistance) * Mathf.Sign(Vector3.Dot(endB - m, up));

                    float triangle = Mathf.Abs(RelativeIntensityAlongLine(x1, minimumDistance) - RelativeIntensityAlongLine(x0, minimumDistance));

                    light.light.color = color;
                    light.light.intensity = color.a * light.intensity * triangle;
                }
                else
                {
                    light.light.color = Color.black;
                    light.light.intensity = 0;
                }
            }
        }

        private float RelativeIntensityAlongLine(float x, float h)
        {
            // integral is ∫ 1 / (h^2 + x^2) dx = atan(x / h) / h + c
            return Mathf.Atan(x / h) / h;
        }

        private void AddPointLight(Color color, Transform parent)
        {
            Light light = new GameObject(parent.name + "Light").AddComponent<Light>();

            light.type = LightType.Point;
            light.color = color;
            light.intensity = 0.35f;
            light.shadows = LightShadows.Hard;
            light.range = 5;
            light.renderMode = LightRenderMode.ForcePixel;
            light.cullingMask = AvatarLayers.kAllLayersMask;

            light.transform.SetParent(parent, false);
            light.transform.localPosition = new Vector3(0, 0, 0.5f); // middle of saber
            light.transform.rotation = Quaternion.identity;
        }

        private struct GameLight
        {
            public readonly TubeBloomPrePassLight tubeLight;
            public readonly Light light;
            public readonly float intensity;
            public readonly Vector3 offset;

            public readonly float width;
            public readonly float length;
            public readonly float center;

            public GameLight(TubeBloomPrePassLight tubeLight, Light light)
            {
                this.tubeLight = tubeLight;
                this.light = light;

                width = tubeLight.GetPrivateField<float>("_width");
                length = tubeLight.GetPrivateField<float>("_length");
                center = tubeLight.GetPrivateField<float>("_center");
                intensity = width * tubeLight.GetPrivateField<float>("_colorAlphaMultiplier") * tubeLight.GetPrivateField<float>("_bloomFogIntensityMultiplier");

                offset = (0.5f - center) * length * Vector3.up;
            }
        }
    }
}
