import React from "react";
import { splitLinks } from "./utils/linkify";

interface LinkifiedTextProps {
  text: string;
}

const LinkifiedText: React.FC<LinkifiedTextProps> = ({ text }) => (
  <>
    {splitLinks(text).map((segment, index) =>
      segment.kind === "link" ? (
        <a key={index} href={segment.href} target="_blank" rel="noopener noreferrer" className="underline">
          {segment.text}
        </a>
      ) : (
        <React.Fragment key={index}>{segment.text}</React.Fragment>
      )
    )}
  </>
);

export default LinkifiedText;
