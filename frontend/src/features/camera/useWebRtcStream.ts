import { useEffect, useRef, useState } from 'react'

/**
 * Same-origin WebRTC signaling endpoint. nginx proxies `/go2rtc/` to the local
 * go2rtc API; only the SDP offer/answer goes through it — the media itself
 * flows directly to the Pi on UDP/TCP 8555 (WebRTC).
 */
export const WEBRTC_SIGNALING_URL = '/go2rtc/api/webrtc?src=cam'

/**
 * If WebRTC has not reached `live` within this window (go2rtc missing, ICE
 * failure, slow browser), the feed gives up and the caller falls back to the
 * MJPEG `<img>`.
 */
export const WEBRTC_FALLBACK_TIMEOUT_MS = 5000

/**
 * A WebRTC track that is `connected` but whose media clock stops advancing (for
 * example after a missed keyframe) must not freeze the UI forever: after this
 * long without progress the feed gives up and falls back to MJPEG.
 */
export const WEBRTC_STALL_TIMEOUT_MS = 4000
const STALL_TICK_MS = 1000

export type WebRtcPhase = 'unavailable' | 'connecting' | 'live' | 'failed'

function webRtcSupported(): boolean {
  return typeof RTCPeerConnection !== 'undefined'
}

/**
 * Low-latency WebRTC feed for the real (ustreamer) camera mode. The browser
 * negotiates a recvonly H.264 track with go2rtc (WHEP-style: POST the SDP
 * offer, read the answer) and renders it in a `<video>`. When WebRTC is not
 * available (jsdom tests, older browsers, or go2rtc absent) the phase settles
 * on `unavailable`, and when it fails/is too slow on `failed` — the caller then
 * falls back to the MJPEG `<img>`.
 */
export function useWebRtcStream(enabled: boolean) {
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const [result, setResult] = useState<'connecting' | 'live' | 'failed'>('connecting')

  useEffect(() => {
    if (!enabled || !webRtcSupported()) return

    let cancelled = false
    let settled = false
    const pc = new RTCPeerConnection({ iceServers: [] })

    const giveUp = () => {
      if (cancelled || settled) return
      settled = true
      setResult('failed')
    }
    const fallbackTimer = window.setTimeout(() => {
      if (pc.connectionState !== 'connected') giveUp()
    }, WEBRTC_FALLBACK_TIMEOUT_MS)

    pc.addTransceiver('video', { direction: 'recvonly' })
    pc.ontrack = (event) => {
      const [stream] = event.streams
      if (videoRef.current && stream) {
        videoRef.current.srcObject = stream
      }
    }
    pc.onconnectionstatechange = () => {
      if (cancelled || settled) return
      if (pc.connectionState === 'connected') {
        settled = true
        window.clearTimeout(fallbackTimer)
        setResult('live')
      } else if (pc.connectionState === 'failed') {
        giveUp()
      }
    }

    // Stall watchdog: a connected-but-frozen track must fall back, not freeze.
    let lastTime = -1
    let stalledFor = 0
    const stallTimer = window.setInterval(() => {
      if (cancelled || settled) return
      const video = videoRef.current
      if (!video) return
      if (video.currentTime === lastTime) {
        stalledFor += STALL_TICK_MS
        if (stalledFor >= WEBRTC_STALL_TIMEOUT_MS) giveUp()
      } else {
        lastTime = video.currentTime
        stalledFor = 0
      }
    }, STALL_TICK_MS)

    void (async () => {
      try {
        const offer = await pc.createOffer()
        await pc.setLocalDescription(offer)
        const response = await fetch(WEBRTC_SIGNALING_URL, {
          method: 'POST',
          headers: { 'Content-Type': 'application/sdp' },
          body: offer.sdp,
        })
        const answerSdp = await response.text()
        // A non-SDP body means the endpoint is not go2rtc (for example an SPA
        // fallback): treat it as unavailable and fall back to MJPEG.
        if (!response.ok || !answerSdp.startsWith('v=')) {
          giveUp()
          return
        }
        if (cancelled) return
        await pc.setRemoteDescription({ type: 'answer', sdp: answerSdp })
      } catch {
        giveUp()
      }
    })()

    return () => {
      cancelled = true
      window.clearTimeout(fallbackTimer)
      window.clearInterval(stallTimer)
      pc.close()
    }
  }, [enabled])

  const phase: WebRtcPhase = !enabled || !webRtcSupported() ? 'unavailable' : result
  return { videoRef, phase }
}
