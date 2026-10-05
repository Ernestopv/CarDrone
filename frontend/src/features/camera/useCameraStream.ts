import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * The reserved, same-origin media-plane path (Task 31/33): the browser always
 * uses this constant — never a hostname, never an IP, never an env variable.
 */
export const CAMERA_STREAM_PATH = '/camera/'

/** Backoff bounds for the fresh-GET retry (Task 33; specs/integration/camera-stream.md). */
export const RETRY_START_MS = 1000
export const RETRY_MAX_MS = 5000

export type CameraStreamPhase = 'idle' | 'loading' | 'live' | 'error'

/**
 * MJPEG feed lifecycle for real (ustreamer) camera mode.
 *
 * `enabled` is true only while the camera status is `streaming` in ustreamer
 * mode. A "reconnect" is a fresh `GET /camera/` — MJPEG is stateless per
 * request (Task 31). The phase is *derived*: disabled → `idle`; a mounted feed
 * that has not errored and not rendered → `loading`; first frame (`load`) →
 * `live`; an image error while still enabled → `error` with a fresh GET
 * scheduled after bounded backoff. Every fresh mount (enable or retry)
 * resets the feed state through the image ref callback, so no synchronous
 * setState happens inside effects.
 */
export function useCameraStream(enabled: boolean) {
  const [frameVersion, setFrameVersion] = useState(0)
  const [live, setLive] = useState(false)
  const [failed, setFailed] = useState(false)

  const retryDelay = useRef(RETRY_START_MS)
  const timer = useRef<number | undefined>(undefined)

  const clearTimer = useCallback(() => {
    if (timer.current !== undefined) {
      window.clearTimeout(timer.current)
      timer.current = undefined
    }
  }, [])

  // Called on every fresh <img> mount (feed enabled or retry remount): the new
  // GET starts clean — no error, no live claim, backoff ladder reset.
  const onFeedMount = useCallback(() => {
    setLive(false)
    setFailed(false)
    retryDelay.current = RETRY_START_MS
  }, [])

  // The image decoded its first frame: live. Reset the backoff for the next
  // failure cycle. No latch: a later error returns to the error/retry path.
  const onFrame = useCallback(() => {
    clearTimer()
    retryDelay.current = RETRY_START_MS
    setLive(true)
    setFailed(false)
  }, [clearTimer])

  // The current GET failed while the feed is still enabled: surface `error`
  // and schedule one fresh GET after backoff (1s → 2s → 4s, capped).
  const onFeedError = useCallback(() => {
    clearTimer()
    setLive(false)
    setFailed(true)
    const delay = Math.min(retryDelay.current, RETRY_MAX_MS)
    retryDelay.current = Math.min(retryDelay.current * 2, RETRY_MAX_MS)
    timer.current = window.setTimeout(() => {
      timer.current = undefined
      setLive(false)
      setFailed(false)
      setFrameVersion((version) => version + 1)
    }, delay)
  }, [clearTimer])

  // Disabling (status leaves streaming / mode not ustreamer) cancels any
  // pending retry. No state is set here: the derived phase falls back to
  // `idle`, and the next enable mounts a fresh <img> that resets via onFeedMount.
  useEffect(() => {
    if (!enabled) {
      clearTimer()
      retryDelay.current = RETRY_START_MS
    }
  }, [enabled, clearTimer])

  // Unmount: never leave a timer running against an unmounted component.
  useEffect(() => clearTimer, [clearTimer])

  const phase: CameraStreamPhase = !enabled ? 'idle' : failed ? 'error' : live ? 'live' : 'loading'

  return { phase, frameVersion, onFrame, onFeedError, onFeedMount }
}