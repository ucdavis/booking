import packageInfo from '../../package.json' with { type: 'json' };

export function GeometricFooter() {
  return (
    <footer
      className="relative overflow-hidden bg-base-100 py-12"
      role="contentinfo"
    >
      <svg
        aria-hidden="true"
        className="pointer-events-none absolute bottom-0 left-0 hidden w-[15.3rem] md:block"
        viewBox="0 0 278 249"
      >
        <g transform="translate(0 10)">
          <g transform="translate(139 114.5) rotate(-101) translate(-139 -114.5) translate(36 -7)">
            <circle
              className="footer-gold-sphere"
              cx="84"
              cy="126"
              fill="#ffbf00"
              r="60.69"
            />
            <circle
              className="footer-gold-sphere footer-float-up"
              cx="140.525"
              cy="85.625"
              fill="#ffdc00"
              fillOpacity="0.8"
              r="40.09875"
            />
            <circle
              className="footer-gold-sphere"
              cx="65.3"
              cy="197.4"
              fill="#f18a00"
              fillOpacity="0.8"
              r="23.8425"
            />
            <circle
              className="footer-gold-sphere footer-float-diagonal"
              cx="83.5"
              cy="11.5"
              fill="#ffbf00"
              r="8.30875"
            />
          </g>
        </g>
      </svg>

      <div className="relative mx-auto flex max-w-5xl flex-col items-center gap-5 px-6 text-center">
        <a
          className="block"
          href="https://caes.ucdavis.edu/"
          rel="noopener noreferrer"
          target="_blank"
        >
          <img
            alt="UC Davis College of Agricultural and Environmental Sciences"
            className="w-56 sm:w-64"
            src="/caes.svg"
          />
        </a>
        <div className="space-y-1">
          <p className="text-sm text-base-content">
            © 2026–2027 Regents of the University of California
          </p>
          <p className="text-xs text-base-content/65">
            Version {packageInfo.version}
          </p>
        </div>
      </div>

      <svg
        aria-hidden="true"
        className="pointer-events-none absolute right-1 bottom-2 hidden w-[13.6rem] md:block"
        viewBox="0 0 225 179"
      >
        <g transform="translate(-1566 -30)">
          <g transform="translate(1566 30)">
            <ellipse
              className="footer-blue-sphere"
              cx="122.074468"
              cy="78.1303191"
              fill="#002855"
              rx="56.205119687"
              ry="56.44915554975"
            />
            <circle
              className="footer-blue-sphere footer-float-left"
              cx="73.24468086"
              cy="129.281782215"
              fill="#008eaa"
              fillOpacity="0.8"
              r="29.3996010915"
            />
            <circle
              className="footer-blue-sphere"
              cx="180.0771279"
              cy="49.35917553"
              fill="#0047ba"
              fillOpacity="0.8"
              r="24.21143617175"
            />
            <circle
              className="footer-blue-sphere footer-float-diagonal-reverse"
              cx="16.7553191"
              cy="34.7074468"
              fill="#002855"
              r="12.10571804975"
            />
          </g>
        </g>
      </svg>
    </footer>
  );
}
